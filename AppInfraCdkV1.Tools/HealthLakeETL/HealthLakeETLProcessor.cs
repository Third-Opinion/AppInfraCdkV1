using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Glue;
using Amazon.Glue.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace AppInfraCdkV1.Tools.HealthLakeETL
{
    /// <summary>
    /// ETL Processor for HealthLake FHIR exports with multi-tenant support.
    /// Reads NDJSON files exported from HealthLake, extracts tenant information
    /// from security labels, and partitions data by tenant GUID in S3.
    /// </summary>
    public class HealthLakeETLProcessor
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IAmazonGlue _glueClient;
        private readonly HealthLakeETLConfig _config;
        
        private const string TENANT_CLAIM_SYSTEM = "http://thirdopinion.io/identity/claims/tenant";
        
        public HealthLakeETLProcessor(IAmazonS3 s3Client, IAmazonGlue glueClient, HealthLakeETLConfig config)
        {
            _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
            _glueClient = glueClient ?? throw new ArgumentNullException(nameof(glueClient));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }
        
        /// <summary>
        /// Main processing method that orchestrates the ETL pipeline
        /// </summary>
        public async Task ProcessExportAsync(string exportPath, string timestamp)
        {
            Console.WriteLine($"Starting HealthLake ETL processing");
            Console.WriteLine($"Export path: {exportPath}");
            Console.WriteLine($"Multi-tenant mode: {_config.EnableMultiTenant}");
            
            var resourceTypes = new[]
            {
                "Patient",
                "Observation",
                "Condition",
                "MedicationRequest",
                "Procedure",
                "DiagnosticReport",
                "Encounter",
                "AllergyIntolerance",
                "Immunization",
                "CarePlan",
                "Goal",
                "MedicationStatement"
            };
            
            foreach (var resourceType in resourceTypes)
            {
                try
                {
                    await ProcessResourceTypeAsync(resourceType, exportPath, timestamp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to process {resourceType}: {ex.Message}");
                    // Continue with other resource types even if one fails
                }
            }
            
            Console.WriteLine("ETL processing completed successfully");
        }
        
        /// <summary>
        /// Process a specific FHIR resource type and partition by tenant
        /// </summary>
        private async Task ProcessResourceTypeAsync(string resourceType, string exportPath, string timestamp)
        {
            Console.WriteLine($"Processing {resourceType} from {exportPath}");
            
            // Parse S3 path
            var exportUri = new Uri(exportPath);
            var bucketName = exportUri.Host;
            var prefix = exportUri.AbsolutePath.TrimStart('/');
            var resourcePrefix = Path.Combine(prefix, resourceType).Replace('\\', '/');
            
            // List all NDJSON files for this resource type
            var listRequest = new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = resourcePrefix,
                Delimiter = "/"
            };
            
            var response = await _s3Client.ListObjectsV2Async(listRequest);
            
            if (response.S3Objects == null || !response.S3Objects.Any())
            {
                Console.WriteLine($"No data found for {resourceType}");
                return;
            }
            
            // Process each NDJSON file
            var tenantData = new Dictionary<string, List<JObject>>();
            
            foreach (var s3Object in response.S3Objects.Where(o => o.Key.EndsWith(".ndjson")))
            {
                await ProcessNdjsonFileAsync(bucketName, s3Object.Key, tenantData);
            }
            
            // Write partitioned data to S3 as Parquet
            foreach (var (tenantId, resources) in tenantData)
            {
                await WriteParquetToS3Async(resourceType, tenantId, resources, timestamp);
            }
            
            // Create or update Glue catalog table
            await CreateOrUpdateGlueTableAsync(resourceType.ToLower());
        }
        
        /// <summary>
        /// Process a single NDJSON file and extract tenant information
        /// </summary>
        private async Task ProcessNdjsonFileAsync(string bucketName, string key, Dictionary<string, List<JObject>> tenantData)
        {
            var getRequest = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };
            
            using var response = await _s3Client.GetObjectAsync(getRequest);
            using var reader = new StreamReader(response.ResponseStream);
            
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                
                try
                {
                    var resource = JObject.Parse(line);
                    var tenantId = ExtractTenantId(resource);
                    
                    // Add processing metadata
                    resource["_export_timestamp"] = _config.Timestamp;
                    resource["_processing_date"] = DateTime.UtcNow.ToString("O");
                    
                    if (!tenantData.ContainsKey(tenantId))
                    {
                        tenantData[tenantId] = new List<JObject>();
                    }
                    
                    tenantData[tenantId].Add(resource);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Error parsing JSON line: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Extract tenant ID from FHIR security labels
        /// </summary>
        private string ExtractTenantId(JObject resource)
        {
            if (!_config.EnableMultiTenant)
            {
                return "default";
            }
            
            try
            {
                var metaToken = resource["meta"];
                if (metaToken != null)
                {
                    var security = metaToken["security"] as JArray;
                    if (security != null)
                    {
                        foreach (var label in security)
                        {
                            if (label["system"]?.ToString() == TENANT_CLAIM_SYSTEM)
                            {
                                return label["code"]?.ToString() ?? "unknown";
                            }
                        }
                    }
                }
            }
            catch
            {
                // If we can't extract tenant ID, use "unknown"
            }
            
            return "unknown";
        }
        
        /// <summary>
        /// Write processed data to S3 as Parquet files
        /// </summary>
        private async Task WriteParquetToS3Async(string resourceType, string tenantId, List<JObject> resources, string timestamp)
        {
            if (!resources.Any())
                return;
            
            var outputKey = $"{resourceType.ToLower()}/{_config.TenantPartitionKey}={tenantId}/_export_timestamp={timestamp}/data.parquet";
            
            // Create Parquet schema dynamically based on FHIR resource structure
            var schema = CreateParquetSchema(resources.First());
            
            using var memoryStream = new MemoryStream();
            
            // Write Parquet file
            using (var parquetWriter = await ParquetWriter.CreateAsync(schema, memoryStream))
            {
                parquetWriter.CompressionMethod = CompressionMethod.Snappy;
                
                using var groupWriter = parquetWriter.CreateRowGroup();
                
                // Convert JSON objects to columnar format
                var columns = ConvertToColumnarFormat(resources, schema);
                
                for (int i = 0; i < schema.Fields.Count; i++)
                {
                    var dataField = schema.Fields[i] as DataField;
                    if (dataField != null)
                    {
                        await groupWriter.WriteColumnAsync(new DataColumn(
                            dataField,
                            columns[i]
                        ));
                    }
                }
            }
            
            // Upload to S3
            memoryStream.Position = 0;
            var putRequest = new PutObjectRequest
            {
                BucketName = _config.CuratedBucket,
                Key = outputKey,
                InputStream = memoryStream,
                ContentType = "application/x-parquet",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };
            
            await _s3Client.PutObjectAsync(putRequest);
            
            Console.WriteLine($"Successfully wrote {resourceType} for tenant {tenantId} to s3://{_config.CuratedBucket}/{outputKey}");
        }
        
        /// <summary>
        /// Create Parquet schema from FHIR resource structure
        /// </summary>
        private ParquetSchema CreateParquetSchema(JObject sampleResource)
        {
            var fields = new List<Field>();
            
            // Add standard FHIR fields
            fields.Add(new DataField<string>("id"));
            fields.Add(new DataField<string>("resourceType"));
            fields.Add(new DataField<string>("meta_versionId", true));
            fields.Add(new DataField<DateTime>("meta_lastUpdated", true));
            
            // Add tenant and processing metadata
            fields.Add(new DataField<string>(_config.TenantPartitionKey));
            fields.Add(new DataField<string>("_export_timestamp"));
            fields.Add(new DataField<DateTime>("_processing_date"));
            
            // Add resource-specific fields (simplified for this example)
            // In production, you'd want to handle nested structures more comprehensively
            foreach (var property in sampleResource.Properties())
            {
                if (property.Name == "id" || property.Name == "resourceType" || property.Name == "meta")
                    continue;
                
                // Serialize complex properties as JSON strings
                fields.Add(new DataField<string>(property.Name, true));
            }
            
            return new ParquetSchema(fields);
        }
        
        /// <summary>
        /// Convert JSON objects to columnar format for Parquet
        /// </summary>
        private List<Array> ConvertToColumnarFormat(List<JObject> resources, ParquetSchema schema)
        {
            var columns = new List<Array>();
            
            foreach (var field in schema.Fields)
            {
                var dataField = field as DataField;
                if (dataField == null) continue;
                
                if (dataField.Name == "id")
                {
                    columns.Add(resources.Select(r => r["id"]?.ToString()).ToArray());
                }
                else if (dataField.Name == "resourceType")
                {
                    columns.Add(resources.Select(r => r["resourceType"]?.ToString()).ToArray());
                }
                else if (dataField.Name == "meta_versionId")
                {
                    columns.Add(resources.Select(r => r["meta"]?["versionId"]?.ToString()).ToArray());
                }
                else if (dataField.Name == "meta_lastUpdated")
                {
                    columns.Add(resources.Select(r => 
                    {
                        var lastUpdated = r["meta"]?["lastUpdated"]?.ToString();
                        return lastUpdated != null ? DateTime.Parse(lastUpdated) : (DateTime?)null;
                    }).ToArray());
                }
                else if (dataField.Name == _config.TenantPartitionKey)
                {
                    columns.Add(resources.Select(r => ExtractTenantId(r)).ToArray());
                }
                else if (dataField.Name == "_export_timestamp")
                {
                    columns.Add(resources.Select(r => r["_export_timestamp"]?.ToString()).ToArray());
                }
                else if (dataField.Name == "_processing_date")
                {
                    columns.Add(resources.Select(r => 
                    {
                        var processingDate = r["_processing_date"]?.ToString();
                        return processingDate != null ? DateTime.Parse(processingDate) : DateTime.UtcNow;
                    }).ToArray());
                }
                else
                {
                    // For other fields, serialize as JSON if complex
                    columns.Add(resources.Select(r =>
                    {
                        var value = r[dataField.Name];
                        if (value == null) return null;
                        if (value is JValue jValue) return jValue.ToString();
                        return value.ToString(Formatting.None);
                    }).ToArray());
                }
            }
            
            return columns;
        }
        
        /// <summary>
        /// Create or update Glue catalog table for the processed data
        /// </summary>
        private async Task CreateOrUpdateGlueTableAsync(string tableName)
        {
            var databaseName = "healthlake_analytics";
            var location = $"s3://{_config.CuratedBucket}/{tableName}/";
            
            var tableInput = new TableInput
            {
                Name = tableName,
                StorageDescriptor = new StorageDescriptor
                {
                    Location = location,
                    InputFormat = "org.apache.hadoop.hive.ql.io.parquet.MapredParquetInputFormat",
                    OutputFormat = "org.apache.hadoop.hive.ql.io.parquet.MapredParquetOutputFormat",
                    SerdeInfo = new SerDeInfo
                    {
                        SerializationLibrary = "org.apache.hadoop.hive.ql.io.parquet.serde.ParquetHiveSerDe",
                        Parameters = new Dictionary<string, string>
                        {
                            ["serialization.format"] = "1"
                        }
                    },
                    Columns = GetTableColumns(tableName),
                    Compressed = true,
                    StoredAsSubDirectories = false
                },
                PartitionKeys = new List<Column>
                {
                    new Column { Name = _config.TenantPartitionKey, Type = "string" },
                    new Column { Name = "_export_timestamp", Type = "string" }
                },
                TableType = "EXTERNAL_TABLE",
                Parameters = new Dictionary<string, string>
                {
                    ["classification"] = "parquet",
                    ["compressionType"] = "snappy",
                    ["typeOfData"] = "file",
                    ["projection.enabled"] = "true",
                    [$"projection.{_config.TenantPartitionKey}.type"] = "injected",
                    ["projection._export_timestamp.type"] = "injected"
                }
            };
            
            try
            {
                await _glueClient.CreateTableAsync(new CreateTableRequest
                {
                    DatabaseName = databaseName,
                    TableInput = tableInput
                });
                Console.WriteLine($"Created Glue table: {tableName}");
            }
            catch (AlreadyExistsException)
            {
                Console.WriteLine($"Table {tableName} already exists, updating...");
                await _glueClient.UpdateTableAsync(new UpdateTableRequest
                {
                    DatabaseName = databaseName,
                    TableInput = tableInput
                });
            }
        }
        
        /// <summary>
        /// Get table columns based on resource type
        /// </summary>
        private List<Column> GetTableColumns(string tableName)
        {
            // Base columns common to all FHIR resources
            var columns = new List<Column>
            {
                new Column { Name = "id", Type = "string" },
                new Column { Name = "resourceType", Type = "string" },
                new Column { Name = "meta_versionId", Type = "string" },
                new Column { Name = "meta_lastUpdated", Type = "timestamp" },
                new Column { Name = "_processing_date", Type = "timestamp" }
            };
            
            // Add resource-specific columns based on table name
            switch (tableName)
            {
                case "patient":
                    columns.AddRange(new[]
                    {
                        new Column { Name = "identifier", Type = "string" },
                        new Column { Name = "active", Type = "boolean" },
                        new Column { Name = "name", Type = "string" },
                        new Column { Name = "gender", Type = "string" },
                        new Column { Name = "birthDate", Type = "date" },
                        new Column { Name = "address", Type = "string" },
                        new Column { Name = "telecom", Type = "string" }
                    });
                    break;
                    
                case "observation":
                    columns.AddRange(new[]
                    {
                        new Column { Name = "status", Type = "string" },
                        new Column { Name = "code", Type = "string" },
                        new Column { Name = "subject", Type = "string" },
                        new Column { Name = "effectiveDateTime", Type = "timestamp" },
                        new Column { Name = "valueQuantity", Type = "string" },
                        new Column { Name = "interpretation", Type = "string" }
                    });
                    break;
                    
                case "condition":
                    columns.AddRange(new[]
                    {
                        new Column { Name = "clinicalStatus", Type = "string" },
                        new Column { Name = "verificationStatus", Type = "string" },
                        new Column { Name = "code", Type = "string" },
                        new Column { Name = "subject", Type = "string" },
                        new Column { Name = "onsetDateTime", Type = "timestamp" },
                        new Column { Name = "recordedDate", Type = "date" }
                    });
                    break;
                    
                // Add more resource types as needed
                default:
                    // Generic columns for unspecified resource types
                    columns.Add(new Column { Name = "data", Type = "string" });
                    break;
            }
            
            return columns;
        }
    }
    
    /// <summary>
    /// Configuration for HealthLake ETL processing
    /// </summary>
    public class HealthLakeETLConfig
    {
        public string RawBucket { get; set; } = string.Empty;
        public string CuratedBucket { get; set; } = string.Empty;
        public string TenantPartitionKey { get; set; } = "tenantGuid";
        public bool EnableMultiTenant { get; set; } = true;
        public string Timestamp { get; set; } = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    }
}
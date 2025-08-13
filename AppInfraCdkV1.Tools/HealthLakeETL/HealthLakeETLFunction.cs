using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.Glue;
using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace AppInfraCdkV1.Tools.HealthLakeETL
{
    /// <summary>
    /// Lambda function handler for HealthLake ETL processing
    /// This can be triggered by S3 events when HealthLake exports complete
    /// </summary>
    public class HealthLakeETLFunction
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IAmazonGlue _glueClient;
        private readonly HealthLakeETLProcessor _processor;
        
        /// <summary>
        /// Default constructor used by Lambda runtime
        /// </summary>
        public HealthLakeETLFunction() : this(new AmazonS3Client(), new AmazonGlueClient())
        {
        }
        
        /// <summary>
        /// Constructor for dependency injection (useful for testing)
        /// </summary>
        public HealthLakeETLFunction(IAmazonS3 s3Client, IAmazonGlue glueClient)
        {
            _s3Client = s3Client;
            _glueClient = glueClient;
            
            var config = new HealthLakeETLConfig
            {
                RawBucket = Environment.GetEnvironmentVariable("RAW_BUCKET") ?? throw new InvalidOperationException("RAW_BUCKET not configured"),
                CuratedBucket = Environment.GetEnvironmentVariable("CURATED_BUCKET") ?? throw new InvalidOperationException("CURATED_BUCKET not configured"),
                TenantPartitionKey = Environment.GetEnvironmentVariable("TENANT_PARTITION_KEY") ?? "tenantGuid",
                EnableMultiTenant = bool.Parse(Environment.GetEnvironmentVariable("ENABLE_MULTI_TENANT") ?? "true")
            };
            
            _processor = new HealthLakeETLProcessor(_s3Client, _glueClient, config);
        }
        
        /// <summary>
        /// Lambda function handler
        /// </summary>
        public async Task<HealthLakeETLResponse> FunctionHandler(HealthLakeETLRequest request, ILambdaContext context)
        {
            context.Logger.LogLine($"Processing HealthLake export: {JsonConvert.SerializeObject(request)}");
            
            try
            {
                // Validate input
                if (string.IsNullOrEmpty(request.ExportPath))
                {
                    throw new ArgumentException("ExportPath is required");
                }
                
                if (string.IsNullOrEmpty(request.Timestamp))
                {
                    request.Timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                }
                
                // Process the export
                await _processor.ProcessExportAsync(request.ExportPath, request.Timestamp);
                
                return new HealthLakeETLResponse
                {
                    Success = true,
                    Message = "ETL processing completed successfully",
                    ProcessedPath = request.ExportPath,
                    Timestamp = request.Timestamp
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error processing HealthLake export: {ex}");
                
                return new HealthLakeETLResponse
                {
                    Success = false,
                    Message = $"ETL processing failed: {ex.Message}",
                    ProcessedPath = request.ExportPath,
                    Timestamp = request.Timestamp
                };
            }
        }
    }
    
    /// <summary>
    /// Request model for HealthLake ETL Lambda function
    /// </summary>
    public class HealthLakeETLRequest
    {
        public string ExportPath { get; set; } = string.Empty;
        public string ExportJobId { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public Dictionary<string, string>? AdditionalParameters { get; set; }
    }
    
    /// <summary>
    /// Response model for HealthLake ETL Lambda function
    /// </summary>
    public class HealthLakeETLResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ProcessedPath { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
    }
}
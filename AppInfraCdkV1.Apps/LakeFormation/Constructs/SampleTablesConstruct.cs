using Amazon.CDK;
using Amazon.CDK.AWS.Glue;
using Amazon.CDK.AWS.LakeFormation;
using Constructs;
using System.Collections.Generic;
using System.Linq;

namespace AppInfraCdkV1.Apps.LakeFormation.Constructs
{
    public interface ISampleTablesConstructProps
    {
        string Environment { get; }
        string AccountId { get; }
        string DatabaseName { get; }
        string S3BucketName { get; }
        string[] TenantIds { get; }
        bool CreatePHITables { get; }
        bool CreateNonPHITables { get; }
        bool CreateCrosstenantTables { get; }
    }

    public class SampleTablesConstructProps : ISampleTablesConstructProps
    {
        public string Environment { get; set; } = "Development";
        public string AccountId { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = "external_fhir_curated";
        public string S3BucketName { get; set; } = string.Empty;
        public string[] TenantIds { get; set; } = new[] { "tenant-a", "tenant-b", "tenant-c" };
        public bool CreatePHITables { get; set; } = true;
        public bool CreateNonPHITables { get; set; } = true;
        public bool CreateCrosstenantTables { get; set; } = true;
    }

    public class TableDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string S3Location { get; set; } = string.Empty;
        public CfnTable.ColumnProperty[] Columns { get; set; } = new CfnTable.ColumnProperty[0];
        public CfnTable.ColumnProperty[] PartitionKeys { get; set; } = new CfnTable.ColumnProperty[0];
        public Dictionary<string, string> LFTags { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> TableParameters { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// CDK Construct for creating sample Glue tables with comprehensive LF-Tag classification
    /// Supports PHI/non-PHI data, multi-tenant scenarios, and various data types for testing
    /// </summary>
    public class SampleTablesConstruct : Construct
    {
        public Dictionary<string, CfnTable> PHITables { get; private set; }
        public Dictionary<string, CfnTable> NonPHITables { get; private set; }
        public Dictionary<string, CfnTable> CrossTenantTables { get; private set; }
        public Dictionary<string, CfnTagAssociation> TableTagAssociations { get; private set; }

        private readonly ISampleTablesConstructProps _props;
        private readonly LakeFormationTagsConstruct _tagsConstruct;

        public SampleTablesConstruct(
            Construct scope,
            string id,
            ISampleTablesConstructProps props,
            LakeFormationTagsConstruct tagsConstruct)
            : base(scope, id)
        {
            _props = props;
            _tagsConstruct = tagsConstruct;

            PHITables = new Dictionary<string, CfnTable>();
            NonPHITables = new Dictionary<string, CfnTable>();
            CrossTenantTables = new Dictionary<string, CfnTable>();
            TableTagAssociations = new Dictionary<string, CfnTagAssociation>();

            if (_props.CreatePHITables)
                CreatePHITables();

            if (_props.CreateNonPHITables)
                CreateNonPHITables();

            if (_props.CreateCrosstenantTables)
                CreateCrossTenantTables();

            // Add tags to the construct
            Amazon.CDK.Tags.Of(this).Add("Component", "SampleTables");
            Amazon.CDK.Tags.Of(this).Add("Environment", _props.Environment);
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
        }

        /// <summary>
        /// Creates PHI-classified tables for each tenant
        /// </summary>
        private void CreatePHITables()
        {
            foreach (var tenantId in _props.TenantIds)
            {
                // Patient data table (PHI)
                var patientTableDef = new TableDefinition
                {
                    Name = $"patient_data_{tenantId.Replace("-", "_")}",
                    Description = $"Patient FHIR data for {tenantId} - contains PHI",
                    S3Location = $"s3://{_props.S3BucketName}/tenant={tenantId}/phi/patient_data/",
                    Columns = new[]
                    {
                        new CfnTable.ColumnProperty { Name = "patient_id", Type = "string", Comment = "FHIR Patient ID" },
                        new CfnTable.ColumnProperty { Name = "tenant_id", Type = "string", Comment = "Tenant identifier" },
                        new CfnTable.ColumnProperty { Name = "first_name", Type = "string", Comment = "Patient first name (PHI)" },
                        new CfnTable.ColumnProperty { Name = "last_name", Type = "string", Comment = "Patient last name (PHI)" },
                        new CfnTable.ColumnProperty { Name = "date_of_birth", Type = "date", Comment = "Date of birth (PHI)" },
                        new CfnTable.ColumnProperty { Name = "ssn", Type = "string", Comment = "Social Security Number (PHI)" },
                        new CfnTable.ColumnProperty { Name = "address", Type = "string", Comment = "Home address (PHI)" },
                        new CfnTable.ColumnProperty { Name = "phone", Type = "string", Comment = "Phone number (PHI)" },
                        new CfnTable.ColumnProperty { Name = "email", Type = "string", Comment = "Email address (PHI)" },
                        new CfnTable.ColumnProperty { Name = "medical_record_number", Type = "string", Comment = "MRN (PHI)" },
                        new CfnTable.ColumnProperty { Name = "fhir_resource", Type = "string", Comment = "Complete FHIR Patient resource" }
                    },
                    PartitionKeys = new[]
                    {
                        new CfnTable.ColumnProperty { Name = "tenant", Type = "string" },
                        new CfnTable.ColumnProperty { Name = "import_date", Type = "string" }
                    },
                    LFTags = new Dictionary<string, string>
                    {
                        ["PHI"] = "true",
                        ["TenantID"] = tenantId,
                        ["DataType"] = "clinical",
                        ["Sensitivity"] = "restricted",
                        ["SourceSystem"] = "epic",
                        ["Environment"] = _props.Environment.ToLower()
                    },
                    TableParameters = new Dictionary<string, string>
                    {
                        ["classification"] = "json",
                        ["compressionType"] = "gzip",
                        ["typeOfData"] = "file"
                    }
                };

                var patientTable = CreateTable(patientTableDef);
                PHITables[patientTableDef.Name] = patientTable;

                // Clinical observations table (PHI)
                var observationsTableDef = new TableDefinition
                {
                    Name = $"clinical_observations_{tenantId.Replace("-", "_")}",
                    Description = $"Clinical observations and lab results for {tenantId} - contains PHI",
                    S3Location = $"s3://{_props.S3BucketName}/tenant={tenantId}/phi/observations/",
                    Columns = new[]
                    {
                        new CfnTable.ColumnProperty { Name = "observation_id", Type = "string", Comment = "FHIR Observation ID" },
                        new CfnTable.ColumnProperty { Name = "patient_id", Type = "string", Comment = "FHIR Patient ID" },
                        new CfnTable.ColumnProperty { Name = "tenant_id", Type = "string", Comment = "Tenant identifier" },
                        new CfnTable.ColumnProperty { Name = "observation_code", Type = "string", Comment = "LOINC or SNOMED code" },
                        new CfnTable.ColumnProperty { Name = "observation_value", Type = "string", Comment = "Observation value" },
                        new CfnTable.ColumnProperty { Name = "observation_date", Type = "timestamp", Comment = "Date of observation" },
                        new CfnTable.ColumnProperty { Name = "provider_id", Type = "string", Comment = "Healthcare provider ID" },
                        new CfnTable.ColumnProperty { Name = "facility_id", Type = "string", Comment = "Healthcare facility ID" },
                        new CfnTable.ColumnProperty { Name = "fhir_resource", Type = "string", Comment = "Complete FHIR Observation resource" }
                    },
                    PartitionKeys = new[]
                    {
                        new CfnTable.ColumnProperty { Name = "tenant", Type = "string" },
                        new CfnTable.ColumnProperty { Name = "observation_year", Type = "string" },
                        new CfnTable.ColumnProperty { Name = "observation_month", Type = "string" }
                    },
                    LFTags = new Dictionary<string, string>
                    {
                        ["PHI"] = "true",
                        ["TenantID"] = tenantId,
                        ["DataType"] = "clinical",
                        ["Sensitivity"] = "confidential",
                        ["SourceSystem"] = "cerner",
                        ["Environment"] = _props.Environment.ToLower()
                    }
                };

                var observationsTable = CreateTable(observationsTableDef);
                PHITables[observationsTableDef.Name] = observationsTable;
            }
        }

        /// <summary>
        /// Creates non-PHI tables for each tenant
        /// </summary>
        private void CreateNonPHITables()
        {
            foreach (var tenantId in _props.TenantIds)
            {
                // Operational metrics table (non-PHI)
                var metricsTableDef = new TableDefinition
                {
                    Name = $"operational_metrics_{tenantId.Replace("-", "_")}",
                    Description = $"System operational metrics for {tenantId} - no PHI",
                    S3Location = $"s3://{_props.S3BucketName}/tenant={tenantId}/non-phi/metrics/",
                    Columns = new[]
                    {
                        new CfnTable.ColumnProperty { Name = "metric_id", Type = "string", Comment = "Unique metric identifier" },
                        new CfnTable.ColumnProperty { Name = "tenant_id", Type = "string", Comment = "Tenant identifier" },
                        new CfnTable.ColumnProperty { Name = "metric_name", Type = "string", Comment = "Name of the metric" },
                        new CfnTable.ColumnProperty { Name = "metric_value", Type = "double", Comment = "Metric value" },
                        new CfnTable.ColumnProperty { Name = "metric_timestamp", Type = "timestamp", Comment = "Timestamp of metric" },
                        new CfnTable.ColumnProperty { Name = "system_component", Type = "string", Comment = "System component generating metric" },
                        new CfnTable.ColumnProperty { Name = "environment", Type = "string", Comment = "Environment (dev/staging/prod)" }
                    },
                    PartitionKeys = new[]
                    {
                        new CfnTable.ColumnProperty { Name = "tenant", Type = "string" },
                        new CfnTable.ColumnProperty { Name = "metric_date", Type = "string" }
                    },
                    LFTags = new Dictionary<string, string>
                    {
                        ["PHI"] = "false",
                        ["TenantID"] = tenantId,
                        ["DataType"] = "operational",
                        ["Sensitivity"] = "internal",
                        ["SourceSystem"] = "healthlake",
                        ["Environment"] = _props.Environment.ToLower()
                    }
                };

                var metricsTable = CreateTable(metricsTableDef);
                NonPHITables[metricsTableDef.Name] = metricsTable;

                // Administrative data table (non-PHI)
                var adminTableDef = new TableDefinition
                {
                    Name = $"administrative_data_{tenantId.Replace("-", "_")}",
                    Description = $"Administrative and billing data for {tenantId} - no PHI",
                    S3Location = $"s3://{_props.S3BucketName}/tenant={tenantId}/non-phi/administrative/",
                    Columns = new[]
                    {
                        new CfnTable.ColumnProperty { Name = "record_id", Type = "string", Comment = "Administrative record ID" },
                        new CfnTable.ColumnProperty { Name = "tenant_id", Type = "string", Comment = "Tenant identifier" },
                        new CfnTable.ColumnProperty { Name = "encounter_id", Type = "string", Comment = "Encounter ID (anonymized)" },
                        new CfnTable.ColumnProperty { Name = "service_code", Type = "string", Comment = "Healthcare service code" },
                        new CfnTable.ColumnProperty { Name = "service_date", Type = "date", Comment = "Date of service" },
                        new CfnTable.ColumnProperty { Name = "billing_amount", Type = "decimal(10,2)", Comment = "Billing amount" },
                        new CfnTable.ColumnProperty { Name = "insurance_type", Type = "string", Comment = "Insurance type" },
                        new CfnTable.ColumnProperty { Name = "facility_type", Type = "string", Comment = "Type of healthcare facility" }
                    },
                    PartitionKeys = new[]
                    {
                        new CfnTable.ColumnProperty { Name = "tenant", Type = "string" },
                        new CfnTable.ColumnProperty { Name = "service_year", Type = "string" },
                        new CfnTable.ColumnProperty { Name = "service_month", Type = "string" }
                    },
                    LFTags = new Dictionary<string, string>
                    {
                        ["PHI"] = "false",
                        ["TenantID"] = tenantId,
                        ["DataType"] = "administrative",
                        ["Sensitivity"] = "confidential",
                        ["SourceSystem"] = "allscripts",
                        ["Environment"] = _props.Environment.ToLower()
                    }
                };

                var adminTable = CreateTable(adminTableDef);
                NonPHITables[adminTableDef.Name] = adminTable;
            }
        }

        /// <summary>
        /// Creates cross-tenant reference tables
        /// </summary>
        private void CreateCrossTenantTables()
        {
            // Medical codes reference table (shared across tenants)
            var codesTableDef = new TableDefinition
            {
                Name = "medical_codes_reference",
                Description = "Medical codes reference data - shared across all tenants",
                S3Location = $"s3://{_props.S3BucketName}/tenant=shared/reference/medical_codes/",
                Columns = new[]
                {
                    new CfnTable.ColumnProperty { Name = "code_id", Type = "string", Comment = "Unique code identifier" },
                    new CfnTable.ColumnProperty { Name = "code_system", Type = "string", Comment = "Code system (ICD-10, LOINC, etc.)" },
                    new CfnTable.ColumnProperty { Name = "code_value", Type = "string", Comment = "Actual code value" },
                    new CfnTable.ColumnProperty { Name = "code_description", Type = "string", Comment = "Human-readable description" },
                    new CfnTable.ColumnProperty { Name = "code_category", Type = "string", Comment = "Category classification" },
                    new CfnTable.ColumnProperty { Name = "effective_date", Type = "date", Comment = "Effective date" },
                    new CfnTable.ColumnProperty { Name = "expiration_date", Type = "date", Comment = "Expiration date" },
                    new CfnTable.ColumnProperty { Name = "version", Type = "string", Comment = "Code system version" }
                },
                PartitionKeys = new[]
                {
                    new CfnTable.ColumnProperty { Name = "code_system", Type = "string" },
                    new CfnTable.ColumnProperty { Name = "version", Type = "string" }
                },
                LFTags = new Dictionary<string, string>
                {
                    ["PHI"] = "false",
                    ["TenantID"] = "shared",
                    ["DataType"] = "reference",
                    ["Sensitivity"] = "public",
                    ["SourceSystem"] = "external-api",
                    ["Environment"] = _props.Environment.ToLower()
                }
            };

            var codesTable = CreateTable(codesTableDef);
            CrossTenantTables[codesTableDef.Name] = codesTable;

            // Research cohort table (multi-tenant, de-identified)
            var researchTableDef = new TableDefinition
            {
                Name = "research_cohort_data",
                Description = "De-identified research data aggregated across tenants",
                S3Location = $"s3://{_props.S3BucketName}/tenant=multi-tenant/research/cohorts/",
                Columns = new[]
                {
                    new CfnTable.ColumnProperty { Name = "cohort_id", Type = "string", Comment = "Research cohort identifier" },
                    new CfnTable.ColumnProperty { Name = "participant_id", Type = "string", Comment = "De-identified participant ID" },
                    new CfnTable.ColumnProperty { Name = "age_group", Type = "string", Comment = "Age group category" },
                    new CfnTable.ColumnProperty { Name = "gender", Type = "string", Comment = "Gender category" },
                    new CfnTable.ColumnProperty { Name = "condition_code", Type = "string", Comment = "Primary condition code" },
                    new CfnTable.ColumnProperty { Name = "treatment_outcome", Type = "string", Comment = "Treatment outcome" },
                    new CfnTable.ColumnProperty { Name = "study_enrollment_date", Type = "date", Comment = "Study enrollment date" },
                    new CfnTable.ColumnProperty { Name = "contributing_tenants", Type = "array<string>", Comment = "List of contributing tenant IDs" }
                },
                PartitionKeys = new[]
                {
                    new CfnTable.ColumnProperty { Name = "cohort_id", Type = "string" },
                    new CfnTable.ColumnProperty { Name = "enrollment_year", Type = "string" }
                },
                LFTags = new Dictionary<string, string>
                {
                    ["PHI"] = "false",
                    ["TenantID"] = "multi-tenant",
                    ["DataType"] = "research",
                    ["Sensitivity"] = "internal",
                    ["SourceSystem"] = "healthlake",
                    ["Environment"] = _props.Environment.ToLower()
                }
            };

            var researchTable = CreateTable(researchTableDef);
            CrossTenantTables[researchTableDef.Name] = researchTable;
        }

        /// <summary>
        /// Creates a Glue table with the specified definition and applies LF-Tags
        /// </summary>
        private CfnTable CreateTable(TableDefinition tableDef)
        {
            var table = new CfnTable(this, $"Table{tableDef.Name}", new CfnTableProps
            {
                CatalogId = _props.AccountId,
                DatabaseName = _props.DatabaseName,
                TableInput = new CfnTable.TableInputProperty
                {
                    Name = tableDef.Name,
                    Description = tableDef.Description,
                    StorageDescriptor = new CfnTable.StorageDescriptorProperty
                    {
                        Location = tableDef.S3Location,
                        InputFormat = "org.apache.hadoop.mapred.TextInputFormat",
                        OutputFormat = "org.apache.hadoop.hive.ql.io.HiveIgnoreKeyTextOutputFormat",
                        SerdeInfo = new CfnTable.SerdeInfoProperty
                        {
                            SerializationLibrary = "org.apache.hadoop.hive.serde2.lazy.LazySimpleSerDe",
                            Parameters = new Dictionary<string, string>
                            {
                                ["field.delim"] = "\t",
                                ["serialization.format"] = "\t"
                            }
                        },
                        Columns = tableDef.Columns,
                        Compressed = false,
                        StoredAsSubDirectories = false
                    },
                    PartitionKeys = tableDef.PartitionKeys,
                    Parameters = new Dictionary<string, string>(tableDef.TableParameters)
                    {
                        ["classification"] = tableDef.TableParameters.GetValueOrDefault("classification", "json"),
                        ["compressionType"] = tableDef.TableParameters.GetValueOrDefault("compressionType", "none"),
                        ["typeOfData"] = tableDef.TableParameters.GetValueOrDefault("typeOfData", "file"),
                        ["CreatedBy"] = "CDK-SampleTablesConstruct",
                        ["Environment"] = _props.Environment
                    }
                }
            });

            // Apply LF-Tags to the table
            if (tableDef.LFTags.Count > 0)
            {
                var tagAssociation = _tagsConstruct.CreateTableTagAssociation(
                    $"Tags{tableDef.Name}",
                    _props.DatabaseName,
                    tableDef.Name,
                    tableDef.LFTags
                );

                tagAssociation.AddDependency(table);
                TableTagAssociations[tableDef.Name] = tagAssociation;
            }

            return table;
        }

        /// <summary>
        /// Helper method to get all created tables
        /// </summary>
        public Dictionary<string, CfnTable> GetAllTables()
        {
            var allTables = new Dictionary<string, CfnTable>();

            foreach (var table in PHITables)
                allTables[$"PHI_{table.Key}"] = table.Value;

            foreach (var table in NonPHITables)
                allTables[$"NonPHI_{table.Key}"] = table.Value;

            foreach (var table in CrossTenantTables)
                allTables[$"CrossTenant_{table.Key}"] = table.Value;

            return allTables;
        }

        /// <summary>
        /// Helper method to get table statistics for monitoring
        /// </summary>
        public Dictionary<string, object> GetTableStatistics()
        {
            return new Dictionary<string, object>
            {
                ["TotalTables"] = PHITables.Count + NonPHITables.Count + CrossTenantTables.Count,
                ["PHITables"] = PHITables.Count,
                ["NonPHITables"] = NonPHITables.Count,
                ["CrossTenantTables"] = CrossTenantTables.Count,
                ["TenantsConfigured"] = _props.TenantIds.Length,
                ["DatabaseName"] = _props.DatabaseName,
                ["Environment"] = _props.Environment
            };
        }
    }
}
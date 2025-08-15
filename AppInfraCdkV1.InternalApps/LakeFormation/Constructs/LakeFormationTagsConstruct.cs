using Amazon.CDK;
using Amazon.CDK.AWS.LakeFormation;
using Constructs;
using System.Collections.Generic;

namespace AppInfraCdkV1.InternalApps.LakeFormation.Constructs
{
    public interface ILakeFormationTagsConstructProps
    {
        string Environment { get; }
        string AccountId { get; }
        string[] TenantIds { get; }
        bool EnablePHITags { get; }
        bool EnableTenantTags { get; }
        bool EnableValidationTags { get; }
        Dictionary<string, string[]> CustomTagValues { get; }
    }

    public class LakeFormationTagsConstructProps : ILakeFormationTagsConstructProps
    {
        public string Environment { get; set; } = "Development";
        public string AccountId { get; set; } = string.Empty;
        public string[] TenantIds { get; set; } = new[] { "tenant-a", "tenant-b", "tenant-c", "shared" };
        public bool EnablePHITags { get; set; } = true;
        public bool EnableTenantTags { get; set; } = true;
        public bool EnableValidationTags { get; set; } = true;
        public Dictionary<string, string[]> CustomTagValues { get; set; } = new Dictionary<string, string[]>();
    }

    /// <summary>
    /// CDK Construct for managing Lake Formation Tags for comprehensive data classification
    /// Supports PHI classification, tenant isolation, data types, and sensitivity levels
    /// </summary>
    public class LakeFormationTagsConstruct : Construct
    {
        public Amazon.CDK.AWS.LakeFormation.CfnTag PHITag { get; private set; }
        public Amazon.CDK.AWS.LakeFormation.CfnTag TenantTag { get; private set; }
        public Amazon.CDK.AWS.LakeFormation.CfnTag DataTypeTag { get; private set; }
        public Amazon.CDK.AWS.LakeFormation.CfnTag SensitivityTag { get; private set; }
        public Amazon.CDK.AWS.LakeFormation.CfnTag EnvironmentTag { get; private set; }
        public Amazon.CDK.AWS.LakeFormation.CfnTag SourceSystemTag { get; private set; }

        private readonly ILakeFormationTagsConstructProps _props;

        public LakeFormationTagsConstruct(Construct scope, string id, ILakeFormationTagsConstructProps props) 
            : base(scope, id)
        {
            _props = props;
            
            CreatePHIClassificationTag();
            CreateTenantClassificationTag();
            CreateDataTypeClassificationTag();
            CreateSensitivityClassificationTag();
            CreateEnvironmentClassificationTag();
            CreateSourceSystemClassificationTag();

            // Add tags to the construct itself for resource management
            Amazon.CDK.Tags.Of(this).Add("Component", "LakeFormationTags");
            Amazon.CDK.Tags.Of(this).Add("Environment", _props.Environment);
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
        }

        /// <summary>
        /// Creates LF-Tag for PHI (Protected Health Information) classification
        /// Values: true, false
        /// </summary>
        private void CreatePHIClassificationTag()
        {
            var phiValues = new[] { "true", "false" };
            
            // Allow custom PHI values if provided
            if (_props.CustomTagValues.ContainsKey("PHI"))
            {
                phiValues = _props.CustomTagValues["PHI"];
            }

            PHITag = new Amazon.CDK.AWS.LakeFormation.CfnTag(this, "PHITag", new CfnTagProps
            {
                TagKey = "PHI",
                TagValues = phiValues
            });
        }

        /// <summary>
        /// Creates LF-Tag for tenant-based data classification and query filtering
        /// Values: tenant-a, tenant-b, tenant-c, shared, multi-tenant
        /// </summary>
        private void CreateTenantClassificationTag()
        {
            var tenantValues = new List<string>(_props.TenantIds);
            
            // Add multi-tenant value for tables that span multiple tenants
            if (!tenantValues.Contains("multi-tenant"))
            {
                tenantValues.Add("multi-tenant");
            }

            // Allow additional custom tenant values
            if (_props.CustomTagValues.ContainsKey("TenantID"))
            {
                foreach (var customValue in _props.CustomTagValues["TenantID"])
                {
                    if (!tenantValues.Contains(customValue))
                    {
                        tenantValues.Add(customValue);
                    }
                }
            }

            TenantTag = new Amazon.CDK.AWS.LakeFormation.CfnTag(this, "TenantTag", new CfnTagProps
            {
                TagKey = "TenantID",
                TagValues = tenantValues.ToArray()
            });
        }

        /// <summary>
        /// Creates LF-Tag for data type classification
        /// Values: clinical, research, operational, administrative, reference
        /// </summary>
        private void CreateDataTypeClassificationTag()
        {
            var dataTypeValues = new[] 
            { 
                "clinical",      // Patient care data (FHIR resources)
                "research",      // De-identified research data
                "operational",   // System operational data
                "administrative", // Business administrative data
                "reference"      // Reference/lookup data
            };

            // Allow custom data type values
            if (_props.CustomTagValues.ContainsKey("DataType"))
            {
                dataTypeValues = _props.CustomTagValues["DataType"];
            }

            DataTypeTag = new Amazon.CDK.AWS.LakeFormation.CfnTag(this, "DataTypeTag", new CfnTagProps
            {
                TagKey = "DataType",
                TagValues = dataTypeValues
            });
        }

        /// <summary>
        /// Creates LF-Tag for data sensitivity classification
        /// Values: public, internal, confidential, restricted
        /// </summary>
        private void CreateSensitivityClassificationTag()
        {
            var sensitivityValues = new[] 
            { 
                "public",        // Publicly available data
                "internal",      // Internal use only
                "confidential",  // Confidential business data
                "restricted"     // Highly restricted (PHI, PII)
            };

            // Allow custom sensitivity values
            if (_props.CustomTagValues.ContainsKey("Sensitivity"))
            {
                sensitivityValues = _props.CustomTagValues["Sensitivity"];
            }

            SensitivityTag = new Amazon.CDK.AWS.LakeFormation.CfnTag(this, "SensitivityTag", new CfnTagProps
            {
                TagKey = "Sensitivity",
                TagValues = sensitivityValues
            });
        }

        /// <summary>
        /// Creates LF-Tag for environment classification
        /// Values: development, staging, production
        /// </summary>
        private void CreateEnvironmentClassificationTag()
        {
            var environmentValues = new[] { "development", "staging", "production" };

            // Allow custom environment values
            if (_props.CustomTagValues.ContainsKey("Environment"))
            {
                environmentValues = _props.CustomTagValues["Environment"];
            }

            EnvironmentTag = new Amazon.CDK.AWS.LakeFormation.CfnTag(this, "EnvironmentTag", new CfnTagProps
            {
                TagKey = "Environment",
                TagValues = environmentValues
            });
        }

        /// <summary>
        /// Creates LF-Tag for source system classification
        /// Values: epic, cerner, allscripts, healthlake, external-api, manual-import
        /// </summary>
        private void CreateSourceSystemClassificationTag()
        {
            var sourceSystemValues = new[] 
            { 
                "epic",          // Epic EHR system
                "cerner",        // Cerner EHR system  
                "allscripts",    // Allscripts EHR system
                "healthlake",    // AWS HealthLake
                "external-api",  // External API sources
                "manual-import", // Manual data imports
                "unknown"        // Unknown or legacy sources
            };

            // Allow custom source system values
            if (_props.CustomTagValues.ContainsKey("SourceSystem"))
            {
                sourceSystemValues = _props.CustomTagValues["SourceSystem"];
            }

            SourceSystemTag = new Amazon.CDK.AWS.LakeFormation.CfnTag(this, "SourceSystemTag", new CfnTagProps
            {
                TagKey = "SourceSystem",
                TagValues = sourceSystemValues
            });
        }

        /// <summary>
        /// Helper method to get all created LF-Tags
        /// </summary>
        public Dictionary<string, Amazon.CDK.AWS.LakeFormation.CfnTag> GetAllTags()
        {
            return new Dictionary<string, Amazon.CDK.AWS.LakeFormation.CfnTag>
            {
                ["PHI"] = PHITag,
                ["TenantID"] = TenantTag,
                ["DataType"] = DataTypeTag,
                ["Sensitivity"] = SensitivityTag,
                ["Environment"] = EnvironmentTag,
                ["SourceSystem"] = SourceSystemTag
            };
        }

        /// <summary>
        /// Helper method to create tag associations for a table resource
        /// </summary>
        public CfnTagAssociation CreateTableTagAssociation(
            string associationId,
            string databaseName, 
            string tableName, 
            Dictionary<string, string> tagValues)
        {
            var lfTagPairs = new List<CfnTagAssociation.LFTagPairProperty>();

            foreach (var tagValue in tagValues)
            {
                lfTagPairs.Add(new CfnTagAssociation.LFTagPairProperty
                {
                    CatalogId = Stack.Of(this).Account,
                    TagKey = tagValue.Key,
                    TagValues = new[] { tagValue.Value }
                });
            }

            return new CfnTagAssociation(this, associationId, new CfnTagAssociationProps
            {
                Resource = new CfnTagAssociation.ResourceProperty
                {
                    Table = new CfnTagAssociation.TableResourceProperty
                    {
                        CatalogId = Stack.Of(this).Account,
                        DatabaseName = databaseName,
                        Name = tableName
                    }
                },
                LfTags = lfTagPairs.ToArray()
            });
        }

        /// <summary>
        /// Helper method to create tag associations for a database resource
        /// </summary>
        public CfnTagAssociation CreateDatabaseTagAssociation(
            string associationId,
            string databaseName,
            Dictionary<string, string> tagValues)
        {
            var lfTagPairs = new List<CfnTagAssociation.LFTagPairProperty>();

            foreach (var tagValue in tagValues)
            {
                lfTagPairs.Add(new CfnTagAssociation.LFTagPairProperty
                {
                    CatalogId = Stack.Of(this).Account,
                    TagKey = tagValue.Key,
                    TagValues = new[] { tagValue.Value }
                });
            }

            return new CfnTagAssociation(this, associationId, new CfnTagAssociationProps
            {
                Resource = new CfnTagAssociation.ResourceProperty
                {
                    Database = new CfnTagAssociation.DatabaseResourceProperty
                    {
                        CatalogId = Stack.Of(this).Account,
                        Name = databaseName
                    }
                },
                LfTags = lfTagPairs.ToArray()
            });
        }
    }
}
using Amazon.CDK;
using Constructs;
using AppInfraCdkV1.InternalApps.LakeFormation.Constructs;

namespace AppInfraCdkV1.InternalApps.LakeFormation.Stacks
{
    /// <summary>
    /// Stack for creating test HealthLake instances with single-tenant architecture
    /// Each HealthLake instance is dedicated to a single tenant/customer
    /// </summary>
    public class HealthLakeTestInstanceStack : Stack
    {
        public HealthLakeTestInstanceConstruct HealthLakeInstance { get; private set; }
        
        public HealthLakeTestInstanceStack(Construct scope, string id, IStackProps props,
            LakeFormationEnvironmentConfig config, DataLakeStorageStack storageStack)
            : base(scope, id, props)
        {
            // Add explicit dependency on storage stack (needs S3 buckets)
            AddDependency(storageStack);
            
            // Create the test HealthLake instance for this tenant
            HealthLakeInstance = new HealthLakeTestInstanceConstruct(this, "TestHealthLakeInstance",
                config, storageStack.CuratedDataBucket);
            
            // Add stack-level tags
            Amazon.CDK.Tags.Of(this).Add("Component", "HealthLakeTestInstance");
            Amazon.CDK.Tags.Of(this).Add("TenantId", config.HealthLake.TenantId);
            Amazon.CDK.Tags.Of(this).Add("TenantName", config.HealthLake.TenantName);
            Amazon.CDK.Tags.Of(this).Add("Architecture", "SingleTenant");
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
            
            // Stack description
            this.TemplateOptions.Description = $"HealthLake test instance for tenant {config.HealthLake.TenantName} ({config.HealthLake.TenantId}) in {config.Environment}";
        }
    }
}
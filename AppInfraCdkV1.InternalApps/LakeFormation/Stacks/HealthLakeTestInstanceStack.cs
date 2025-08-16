using Amazon.CDK;
using Constructs;
using AppInfraCdkV1.InternalApps.LakeFormation.Constructs;
using System.Collections.Generic;
using System.Linq;

namespace AppInfraCdkV1.InternalApps.LakeFormation.Stacks
{
    /// <summary>
    /// Stack for creating test HealthLake instances with single-tenant architecture
    /// Each HealthLake instance is dedicated to a single tenant/customer
    /// </summary>
    public class HealthLakeTestInstanceStack : Stack
    {
        public List<HealthLakeTestInstanceConstruct> HealthLakeInstances { get; private set; } = new();
        
        public HealthLakeTestInstanceStack(Construct scope, string id, IStackProps props,
            LakeFormationEnvironmentConfig config, DataLakeStorageStack storageStack)
            : base(scope, id, props)
        {
            // Add explicit dependency on storage stack (needs S3 buckets)
            AddDependency(storageStack);
            
            // Create test HealthLake instances for each tenant
            for (int i = 0; i < config.HealthLake.Count; i++)
            {
                var healthLakeConfig = config.HealthLake[i];
                var instanceId = $"TestHealthLakeInstance{i + 1}";
                
                var instance = new HealthLakeTestInstanceConstruct(this, instanceId,
                    config, healthLakeConfig, storageStack.CuratedDataBucket);
                
                HealthLakeInstances.Add(instance);
            }
            
            // Add stack-level tags
            Amazon.CDK.Tags.Of(this).Add("Component", "HealthLakeTestInstance");
            Amazon.CDK.Tags.Of(this).Add("Architecture", "MultiTenant");
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
            Amazon.CDK.Tags.Of(this).Add("InstanceCount", config.HealthLake.Count.ToString());
            
            // Stack description
            var tenantNames = string.Join(", ", config.HealthLake.Select(h => h.TenantName));
            this.TemplateOptions.Description = $"HealthLake test instances for tenants: {tenantNames} in {config.Environment}";
        }
    }
}
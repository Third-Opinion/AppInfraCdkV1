using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Logs;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Stacks.Services;

/// <summary>
/// Service for discovering and importing shared environment resources
/// </summary>
public class EnvironmentResourceProvider
{
    private readonly DeploymentContext _context;
    private readonly Construct _scope;
    private readonly Dictionary<string, object> _resourceCache = new();

    public EnvironmentResourceProvider(Construct scope, DeploymentContext context)
    {
        _scope = scope;
        _context = context;
    }

    /// <summary>
    /// Gets the shared VPC for the environment
    /// </summary>
    public IVpc GetSharedVpc()
    {
        var cacheKey = "shared-vpc";
        
        if (_resourceCache.ContainsKey(cacheKey))
            return (IVpc)_resourceCache[cacheKey];

        // Import VPC attributes from base stack exports
        var vpcId = Fn.ImportValue($"{_context.Environment.Name}-vpc-id");
        var vpcCidr = Fn.ImportValue($"{_context.Environment.Name}-vpc-cidr");
        var availabilityZones = Fn.Split(",", Fn.ImportValue($"{_context.Environment.Name}-vpc-azs"));
        var publicSubnetIds = Fn.Split(",", Fn.ImportValue($"{_context.Environment.Name}-public-subnet-ids"));
        var privateSubnetIds = Fn.Split(",", Fn.ImportValue($"{_context.Environment.Name}-private-subnet-ids"));
        var isolatedSubnetIds = Fn.Split(",", Fn.ImportValue($"{_context.Environment.Name}-isolated-subnet-ids"));
        
        // Use FromVpcAttributes instead of FromLookup (works with CloudFormation tokens)
        var vpc = Vpc.FromVpcAttributes(_scope, "SharedVpc", new VpcAttributes
        {
            VpcId = vpcId,
            VpcCidrBlock = vpcCidr,
            AvailabilityZones = availabilityZones,
            PublicSubnetIds = publicSubnetIds,
            PrivateSubnetIds = privateSubnetIds,
            IsolatedSubnetIds = isolatedSubnetIds
        });
        
        _resourceCache[cacheKey] = vpc;
        return vpc;
    }

    /// <summary>
    /// Gets shared security group by purpose
    /// </summary>
    public ISecurityGroup GetSharedSecurityGroup(string purpose)
    {
        var cacheKey = $"shared-sg-{purpose}";
        
        if (_resourceCache.ContainsKey(cacheKey))
            return (ISecurityGroup)_resourceCache[cacheKey];

        var sgId = Fn.ImportValue($"{_context.Environment.Name}-sg-{purpose}-id");
        
        var sg = SecurityGroup.FromSecurityGroupId(_scope, $"SharedSg{purpose.ToUpperInvariant()}", sgId);
        
        _resourceCache[cacheKey] = sg;
        return sg;
    }

    /// <summary>
    /// Gets the ALB security group
    /// </summary>
    public ISecurityGroup GetAlbSecurityGroup()
    {
        return GetSharedSecurityGroup("alb");
    }

    /// <summary>
    /// Gets the ECS security group
    /// </summary>
    public ISecurityGroup GetEcsSecurityGroup()
    {
        return GetSharedSecurityGroup("ecs");
    }

    /// <summary>
    /// Gets the RDS security group
    /// </summary>
    public ISecurityGroup GetRdsSecurityGroup()
    {
        return GetSharedSecurityGroup("rds");
    }

    /// <summary>
    /// Gets the bastion security group
    /// </summary>
    public ISecurityGroup GetBastionSecurityGroup()
    {
        return GetSharedSecurityGroup("bastion");
    }

    /// <summary>
    /// Gets public subnets from shared VPC
    /// </summary>
    public ISubnet[] GetPublicSubnets()
    {
        // Get subnets directly from the shared VPC (already imported via VPC attributes)
        return GetSharedVpc().PublicSubnets.ToArray();
    }

    /// <summary>
    /// Gets private subnets from shared VPC
    /// </summary>
    public ISubnet[] GetPrivateSubnets()
    {
        // Get subnets directly from the shared VPC (already imported via VPC attributes)
        return GetSharedVpc().PrivateSubnets.ToArray();
    }

    /// <summary>
    /// Gets isolated/database subnets from shared VPC
    /// </summary>
    public ISubnet[] GetIsolatedSubnets()
    {
        // Get subnets directly from the shared VPC (already imported via VPC attributes)
        return GetSharedVpc().IsolatedSubnets.ToArray();
    }

    /// <summary>
    /// Gets shared log group
    /// </summary>
    public ILogGroup GetSharedLogGroup()
    {
        var cacheKey = "shared-log-group";
        
        if (_resourceCache.ContainsKey(cacheKey))
            return (ILogGroup)_resourceCache[cacheKey];

        var logGroupName = Fn.ImportValue($"{_context.Environment.Name}-shared-log-group-name");
        
        var logGroup = LogGroup.FromLogGroupName(_scope, "SharedLogGroup", logGroupName);
        
        _resourceCache[cacheKey] = logGroup;
        return logGroup;
    }

    /// <summary>
    /// Creates subnet selection for private subnets
    /// </summary>
    public SubnetSelection GetPrivateSubnetSelection()
    {
        return new SubnetSelection
        {
            Subnets = GetPrivateSubnets()
        };
    }

    /// <summary>
    /// Creates subnet selection for public subnets
    /// </summary>
    public SubnetSelection GetPublicSubnetSelection()
    {
        return new SubnetSelection
        {
            Subnets = GetPublicSubnets()
        };
    }

    /// <summary>
    /// Creates subnet selection for isolated/database subnets
    /// </summary>
    public SubnetSelection GetIsolatedSubnetSelection()
    {
        return new SubnetSelection
        {
            Subnets = GetIsolatedSubnets()
        };
    }

    /// <summary>
    /// Validates that all expected shared resources are available
    /// </summary>
    public void ValidateSharedResources()
    {
        Console.WriteLine("🔍 Validating shared environment resources...");
        
        try
        {
            GetSharedVpc();
            Console.WriteLine("   ✅ Shared VPC found");
            
            GetAlbSecurityGroup();
            Console.WriteLine("   ✅ ALB security group found");
            
            GetEcsSecurityGroup();
            Console.WriteLine("   ✅ ECS security group found");
            
            GetRdsSecurityGroup();
            Console.WriteLine("   ✅ RDS security group found");
            
            GetPublicSubnets();
            Console.WriteLine("   ✅ Public subnets found");
            
            GetPrivateSubnets();
            Console.WriteLine("   ✅ Private subnets found");
            
            GetIsolatedSubnets();
            Console.WriteLine("   ✅ Isolated subnets found");
            
            Console.WriteLine("✅ All shared environment resources validated successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Shared resource validation failed: {ex.Message}");
            Console.WriteLine("   Make sure the environment base stack is deployed first");
            throw new InvalidOperationException(
                $"Shared environment resources not available. Deploy the base stack for {_context.Environment.Name} first.", 
                ex);
        }
    }
}
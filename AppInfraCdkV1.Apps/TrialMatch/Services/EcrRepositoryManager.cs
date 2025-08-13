using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.CDK;
using Amazon.CDK.AWS.ECR;
using Amazon.ECR;
using Amazon.ECR.Model;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialMatch.Services;

/// <summary>
/// Manages ECR repository operations for the TrialMatch application
/// 
/// This service handles:
/// - ECR repository creation and import
/// - Container name to repository type mapping
/// - Latest image URI retrieval
/// - Repository existence checking
/// - Repository output export
/// </summary>
public class EcrRepositoryManager : Construct
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;
    private readonly Dictionary<string, IRepository> _ecrRepositories = new();

    public EcrRepositoryManager(Construct scope, string id, DeploymentContext context) : base(scope, id)
    {
        _context = context;
        _configLoader = new ConfigurationLoader();
    }

    /// <summary>
    /// Create ECR repositories from configuration
    /// </summary>
    public void CreateEcrRepositories(DeploymentContext context)
    {
        var config = _configLoader.LoadFullConfig(context.Environment.Name);
        
        if (config.EcsConfiguration?.Services != null)
        {
            foreach (var service in config.EcsConfiguration.Services)
            {
                if (service.TaskDefinition != null)
                {
                    foreach (var taskDef in service.TaskDefinition)
                    {
                        if (taskDef.ContainerDefinitions != null)
                        {
                            foreach (var container in taskDef.ContainerDefinitions)
                            {
                                if (container.Name != null)
                                {
                                    // Map container name to repository type
                                    var repositoryType = MapContainerNameToRepositoryType(container.Name);
                                    if (!string.IsNullOrEmpty(repositoryType))
                                    {
                                        var repositoryName = context.Namer.EcrRepository(repositoryType);
                                        var repositoryKey = container.Name; // Use container name as key
                                        
                                        if (!_ecrRepositories.ContainsKey(repositoryKey))
                                        {
                                            _ecrRepositories[repositoryKey] = GetOrCreateEcrRepository(
                                                repositoryType, 
                                                repositoryName, 
                                                $"TrialMatch{repositoryType}EcrRepository"
                                            );
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        
        // Fallback to hardcoded repositories if no configuration found
        if (_ecrRepositories.Count == 0)
        {
            var apiRepositoryName = context.Namer.EcrRepository("api");
            var frontendRepositoryName = context.Namer.EcrRepository("frontend");

            // Create or import API repository
            if (!_ecrRepositories.ContainsKey("api"))
            {
                _ecrRepositories["api"] = GetOrCreateEcrRepository("api", apiRepositoryName, "TrialMatchApiEcrRepository");
            }

            // Create or import frontend repository
            if (!_ecrRepositories.ContainsKey("frontend"))
            {
                _ecrRepositories["frontend"] = GetOrCreateEcrRepository("frontend", frontendRepositoryName, "TrialMatchFrontendEcrRepository");
            }
        }
    }

    /// <summary>
    /// Map container name to repository type based on configuration
    /// </summary>
    public string MapContainerNameToRepositoryType(string containerName)
    {
        // Map container names to repository types based on the configuration
        return containerName.ToLowerInvariant() switch
        {
            "trial-match-api" => "api",
            "trial-match-frontend" => "frontend",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Check if ECR repository has a latest image and return its URI (async version)
    /// </summary>
    public async Task<string?> GetLatestEcrImageUriAsync(string containerName, DeploymentContext context)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(containerName))
        {
            Console.WriteLine($"     ⚠️  Container name is null or empty");
            return null;
        }

        try
        {
            // Map container name to repository type based on configuration
            string repositoryType = MapContainerNameToRepositoryType(containerName);
            if (string.IsNullOrWhiteSpace(repositoryType))
            {
                Console.WriteLine($"     ⚠️  No repository type mapping found for container '{containerName}'");
                return null;
            }

            // Get the ECR repository name using the repository type
            var repositoryName = context.Namer.EcrRepository(repositoryType);
            Console.WriteLine($"     🔍 Using repository type '{repositoryType}' for container '{containerName}' -> repository: {repositoryName}");
            var region = context.Environment.Region;
            var accountId = context.Environment.AccountId;

            Console.WriteLine($"     🔍 Checking for latest image in ECR repository: {repositoryName}");

            // Use AWS SDK to check if repository has latest tag
            using var ecrClient = new AmazonECRClient(new AmazonECRConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region)
            });

            // List images in the repository
            var listImagesRequest = new ListImagesRequest
            {
                RepositoryName = repositoryName
            };

            var listImagesResponse = await ecrClient.ListImagesAsync(listImagesRequest);
            
            if (listImagesResponse.ImageIds == null || listImagesResponse.ImageIds.Count == 0)
            {
                Console.WriteLine($"     ℹ️  No images found in ECR repository: {repositoryName}");
                return null;
            }

            // Check if latest tag exists first, then fall back to the most recently pushed image
            var latestImage = listImagesResponse.ImageIds.FirstOrDefault(img => 
                img.ImageTag != null && img.ImageTag.Equals("latest", StringComparison.OrdinalIgnoreCase));

            if (latestImage == null)
            {
                // Get detailed information about all images to find the most recently pushed one
                var describeImagesRequest = new DescribeImagesRequest
                {
                    RepositoryName = repositoryName,
                    ImageIds = listImagesResponse.ImageIds
                };

                var describeImagesResponse = await ecrClient.DescribeImagesAsync(describeImagesRequest);
                
                if (describeImagesResponse.ImageDetails == null || describeImagesResponse.ImageDetails.Count == 0)
                {
                    Console.WriteLine($"     ℹ️  No image details found in ECR repository: {repositoryName}");
                    return null;
                }

                // Find the most recently pushed image
                var mostRecentImage = describeImagesResponse.ImageDetails
                    .OrderByDescending(img => img.ImagePushedAt)
                    .FirstOrDefault();

                if (mostRecentImage == null)
                {
                    Console.WriteLine($"     ℹ️  No images with push timestamps found in ECR repository: {repositoryName}");
                    return null;
                }

                // Get the tag for the most recent image
                var imageTag = mostRecentImage.ImageTags?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(imageTag))
                {
                    Console.WriteLine($"     ℹ️  Most recent image has no tags in ECR repository: {repositoryName}");
                    return null;
                }

                // Use the most recently pushed image
                var mostRecentImageUri = $"{accountId}.dkr.ecr.{region}.amazonaws.com/{repositoryName}:{imageTag}";
                Console.WriteLine($"     ✅ Found most recently pushed image: {mostRecentImageUri} (pushed at: {mostRecentImage.ImagePushedAt})");
                return mostRecentImageUri;
            }

            // Construct the full image URI for latest tag
            var latestImageUri = $"{accountId}.dkr.ecr.{region}.amazonaws.com/{repositoryName}:latest";
            Console.WriteLine($"     ✅ Found latest image: {latestImageUri}");
            
            return latestImageUri;
        }
        catch (RepositoryNotFoundException)
        {
            Console.WriteLine($"     ⚠️  ECR repository not found for container '{containerName}'");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"     ⚠️  Error checking ECR repository for container '{containerName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if ECR repository has a latest image and return its URI (synchronous wrapper)
    /// </summary>
    public string? GetLatestEcrImageUri(string containerName, DeploymentContext context)
    {
        // Use GetAwaiter().GetResult() instead of Task.Run().Result to avoid potential deadlocks
        return GetLatestEcrImageUriAsync(containerName, context).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Get existing ECR repository or create new one
    /// </summary>
    public IRepository GetOrCreateEcrRepository(string serviceType, string repositoryName, string constructId)
    {
        try
        {
            // Try to import existing repository first
            var existingRepository = Amazon.CDK.AWS.ECR.Repository.FromRepositoryName(this, $"{constructId}Import", repositoryName);
            Console.WriteLine($"✅ Imported existing ECR repository: {repositoryName}");
            return existingRepository;
        }
        catch (Exception)
        {
            // If import fails, create new repository
            var repository = new Amazon.CDK.AWS.ECR.Repository(this, constructId, new RepositoryProps
            {
                RepositoryName = repositoryName,
                ImageScanOnPush = true,
                RemovalPolicy = RemovalPolicy.RETAIN
            });
            Console.WriteLine($"✅ Created new ECR repository: {repositoryName}");
            return repository;
        }
    }

    /// <summary>
    /// Export ECR repository information as CloudFormation outputs
    /// </summary>
    public void ExportEcrRepositoryOutputs()
    {
        foreach (var kvp in _ecrRepositories)
        {
            var repositoryName = kvp.Key;
            var repository = kvp.Value;

            new CfnOutput(this, $"EcrRepositoryArn-{repositoryName}", new CfnOutputProps
            {
                Value = repository.RepositoryArn,
                Description = $"TrialMatch ECR Repository ARN for {repositoryName}",
                ExportName = $"{_context.Environment.Name}-trial-match-{repositoryName}-ecr-repository-arn"
            });
        }
    }

    /// <summary>
    /// Get all created repositories for external access
    /// </summary>
    public Dictionary<string, IRepository> GetCreatedRepositories()
    {
        return new Dictionary<string, IRepository>(_ecrRepositories);
    }

    /// <summary>
    /// Check if a repository has been created for a specific container
    /// </summary>
    public bool HasRepository(string containerName)
    {
        return _ecrRepositories.ContainsKey(containerName);
    }

    /// <summary>
    /// Get a specific repository by container name
    /// </summary>
    public IRepository? GetRepository(string containerName)
    {
        return _ecrRepositories.TryGetValue(containerName, out var repository) ? repository : null;
    }
} 
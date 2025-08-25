using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.CDK;
using Amazon.CDK.AWS.ECR;
using Amazon.ECR;
using Amazon.ECR.Model;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using Constructs;
using System.Linq;

namespace AppInfraCdkV1.Apps.TrialFinderV2.Services;

/// <summary>
/// Manages AWS ECR repository operations for the TrialFinderV2 application
/// 
/// This service handles:
/// - ECR repository existence checking using AWS SDK
/// - ECR repository creation and import
/// - Repository configuration and management
/// - Repository output export
/// </summary>
public class EcrRepositoryManager : Construct
{
    private readonly DeploymentContext _context;
    private readonly Dictionary<string, IRepository> _ecrRepositories = new();

    public EcrRepositoryManager(Construct scope, string id, DeploymentContext context) : base(scope, id)
    {
        _context = context;
    }

    /// <summary>
    /// Create ECR repositories from configuration
    /// </summary>
    public void CreateEcrRepositories(DeploymentContext context)
    {
        var configLoader = new ConfigurationLoader();
        var config = configLoader.LoadFullConfig(context.Environment.Name);
        
        if (config.EcsConfiguration?.TaskDefinition != null)
        {
            foreach (var taskDef in config.EcsConfiguration.TaskDefinition)
            {
                if (taskDef.ContainerDefinitions != null)
                {
                    foreach (var container in taskDef.ContainerDefinitions)
                    {
                        if (container.Repository != null && !string.IsNullOrWhiteSpace(container.Repository.Type))
                        {
                            var repositoryName = context.Namer.EcrRepository(container.Repository.Type);
                            var repositoryKey = container.Name ?? "unknown"; // Use container name as key
                            
                            if (!_ecrRepositories.ContainsKey(repositoryKey))
                            {
                                _ecrRepositories[repositoryKey] = GetOrCreateEcrRepository(
                                    container.Repository.Type, 
                                    repositoryName, 
                                    $"TrialFinder{container.Name}EcrRepository"
                                );
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get existing ECR repository or create new one
    /// </summary>
    public IRepository GetOrCreateEcrRepository(string serviceType, string repositoryName, string constructId)
    {
        // Use AWS SDK to check if repository actually exists
        var region = _context.Environment.Region;
        using var ecrClient = new AmazonECRClient(new AmazonECRConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region)
        });

        try
        {
            // Check if repository exists using AWS SDK
            var describeRequest = new DescribeRepositoriesRequest
            {
                RepositoryNames = new List<string> { repositoryName }
            };
            var describeResponse = Task.Run(() => ecrClient.DescribeRepositoriesAsync(describeRequest)).Result;
            
            if (describeResponse.Repositories != null && describeResponse.Repositories.Count > 0)
            {
                // Repository exists, import it
                var existingRepository = Amazon.CDK.AWS.ECR.Repository.FromRepositoryName(this, $"{constructId}Import", repositoryName);
                Console.WriteLine($"✅ Imported existing ECR repository: {repositoryName}");
                return existingRepository;
            }
            else
            {
                // Repository doesn't exist, create it
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
        catch (RepositoryNotFoundException)
        {
            // Repository doesn't exist, create it
            var repository = new Amazon.CDK.AWS.ECR.Repository(this, constructId, new RepositoryProps
            {
                RepositoryName = repositoryName,
                ImageScanOnPush = true,
                RemovalPolicy = RemovalPolicy.RETAIN
            });
            Console.WriteLine($"✅ Created new ECR repository: {repositoryName}");
            return repository;
        }
        catch (Exception ex)
        {
            // For any other error, assume repository doesn't exist and create it
            Console.WriteLine($"⚠️  Error checking ECR repository '{repositoryName}': {ex.Message}");
            Console.WriteLine($"   Creating new ECR repository: {repositoryName}");
            
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
    /// Check if ECR repository exists using AWS SDK
    /// </summary>
    public async Task<bool> RepositoryExistsAsync(string repositoryName)
    {
        try
        {
            using var ecrClient = new AmazonECRClient();
            var describeRequest = new DescribeRepositoriesRequest
            {
                RepositoryNames = new List<string> { repositoryName }
            };
            
            var response = await ecrClient.DescribeRepositoriesAsync(describeRequest);
            return response.Repositories != null && response.Repositories.Count > 0;
        }
        catch (RepositoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the deployment
            Console.WriteLine($"          ⚠️  Error checking if ECR repository '{repositoryName}' exists: {ex.Message}");
            Console.WriteLine($"          ℹ️  Assuming repository doesn't exist and will create it");
            return false;
        }
    }

    /// <summary>
    /// Check if ECR repository exists using AWS SDK (synchronous version)
    /// </summary>
    public bool RepositoryExists(string repositoryName)
    {
        try
        {
            using var ecrClient = new AmazonECRClient();
            var describeRequest = new DescribeRepositoriesRequest
            {
                RepositoryNames = new List<string> { repositoryName }
            };
            
            var response = ecrClient.DescribeRepositoriesAsync(describeRequest).GetAwaiter().GetResult();
            return response.Repositories != null && response.Repositories.Count > 0;
        }
        catch (RepositoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the deployment
            Console.WriteLine($"          ⚠️  Error checking if ECR repository '{repositoryName}' exists: {ex.Message}");
            Console.WriteLine($"          ℹ️  Assuming repository doesn't exist and will create it");
            return false;
        }
    }

    /// <summary>
    /// Check if ECR repository has a latest image and return its URI
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
    /// Map container name to repository type based on configuration
    /// </summary>
    private string MapContainerNameToRepositoryType(string containerName)
    {
        // Map container names to repository types based on the configuration
        return containerName switch
        {
            "trial-finder-v2" => "webapp",
            "trial-finder-v2-loader" => "loader",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Export ECR repository information for external consumption
    /// </summary>
    public void ExportEcrRepositoryOutputs()
    {
        foreach (var kvp in _ecrRepositories)
        {
            var repositoryName = kvp.Key;
            var repository = kvp.Value;

            // Export ECR Repository ARN with simple, predictable key
            new CfnOutput(this, $"EcrRepositoryArn-{repositoryName}", new CfnOutputProps
            {
                Value = repository.RepositoryArn,
                Description = $"TrialFinderV2 ECR Repository ARN for {repositoryName}",
                ExportName = $"{_context.Environment.Name}-trial-finder-v2-{repositoryName}-ecr-repository-arn"
            });

            // Export ECR Repository Name with simple, predictable key
            new CfnOutput(this, $"EcrRepositoryName-{repositoryName}", new CfnOutputProps
            {
                Value = repository.RepositoryName,
                Description = $"TrialFinderV2 ECR Repository Name for {repositoryName}",
                ExportName = $"{_context.Environment.Name}-trial-finder-v2-{repositoryName}-ecr-repository-name"
            });
        }
    }

    /// <summary>
    /// Get all created ECR repositories for external access
    /// </summary>
    public Dictionary<string, IRepository> GetEcrRepositories()
    {
        return _ecrRepositories;
    }

    /// <summary>
    /// Get a specific ECR repository by key
    /// </summary>
    public IRepository? GetEcrRepository(string key)
    {
        return _ecrRepositories.TryGetValue(key, out var repository) ? repository : null;
    }


}

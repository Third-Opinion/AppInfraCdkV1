using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Google.Apis.Admin.Directory.directory_v1;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;

namespace AppInfraCdkV1.Tools.ScimSync
{
    /// <summary>
    /// Lambda function handler for SCIM synchronization between Google Workspace and AWS Identity Center
    /// </summary>
    public class ScimSyncFunction
    {
        private readonly IAmazonSimpleSystemsManagement _ssmClient;
        private readonly HttpClient _httpClient;
        private ScimSyncConfig _config = null!;
        
        /// <summary>
        /// Default constructor used by Lambda runtime
        /// </summary>
        public ScimSyncFunction() : this(new AmazonSimpleSystemsManagementClient(), new HttpClient())
        {
        }
        
        /// <summary>
        /// Constructor for dependency injection (useful for testing)
        /// </summary>
        public ScimSyncFunction(IAmazonSimpleSystemsManagement ssmClient, HttpClient httpClient)
        {
            _ssmClient = ssmClient;
            _httpClient = httpClient;
        }
        
        /// <summary>
        /// Lambda function handler
        /// </summary>
        public async Task<ScimSyncResponse> FunctionHandler(ScimSyncRequest request, ILambdaContext context)
        {
            context.Logger.LogLine($"Starting SCIM sync: {JsonConvert.SerializeObject(request)}");
            
            try
            {
                // Load configuration from SSM Parameter Store
                await LoadConfiguration(context);
                
                // Check if sync is enabled
                if (!_config.SyncEnabled)
                {
                    context.Logger.LogLine("SCIM sync is disabled");
                    return new ScimSyncResponse
                    {
                        Success = true,
                        Message = "Sync is disabled",
                        SyncedUsers = 0,
                        SyncedGroups = 0
                    };
                }
                
                // Initialize Google Directory API client
                var directoryService = await InitializeGoogleDirectory(context);
                
                // Sync groups
                var syncedGroups = await SyncGroups(directoryService, context);
                
                // Sync users
                var syncedUsers = await SyncUsers(directoryService, context);
                
                context.Logger.LogLine($"Sync completed successfully. Groups: {syncedGroups}, Users: {syncedUsers}");
                
                return new ScimSyncResponse
                {
                    Success = true,
                    Message = "Sync completed successfully",
                    SyncedGroups = syncedGroups,
                    SyncedUsers = syncedUsers,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error during SCIM sync: {ex}");
                
                return new ScimSyncResponse
                {
                    Success = false,
                    Message = $"Sync failed: {ex.Message}",
                    Error = ex.ToString(),
                    Timestamp = DateTime.UtcNow
                };
            }
        }
        
        /// <summary>
        /// Load configuration from SSM Parameter Store
        /// </summary>
        private async Task LoadConfiguration(ILambdaContext context)
        {
            var configPath = Environment.GetEnvironmentVariable("SCIM_SYNC_CONFIG_PATH") ?? "/scim-sync/development";
            context.Logger.LogLine($"Loading configuration from: {configPath}");
            
            var request = new GetParametersByPathRequest
            {
                Path = configPath,
                Recursive = true,
                WithDecryption = true
            };
            
            var response = await _ssmClient.GetParametersByPathAsync(request);
            var parameters = response.Parameters.ToDictionary(p => p.Name.Split('/').Last(), p => p.Value);
            
            _config = new ScimSyncConfig
            {
                GoogleDomain = parameters.GetValueOrDefault("domain", ""),
                GoogleServiceAccountKey = parameters.GetValueOrDefault("service-account-key", ""),
                ScimEndpoint = parameters.GetValueOrDefault("identity-center-scim-endpoint", ""),
                ScimToken = parameters.GetValueOrDefault("identity-center-scim-token", ""),
                GroupFilter = parameters.GetValueOrDefault("group-filters", ".*"),
                SyncEnabled = bool.Parse(parameters.GetValueOrDefault("enabled", "false"))
            };
            
            // Validate configuration
            if (string.IsNullOrEmpty(_config.GoogleDomain))
                throw new InvalidOperationException("Google domain not configured");
            if (string.IsNullOrEmpty(_config.GoogleServiceAccountKey))
                throw new InvalidOperationException("Google service account key not configured");
            if (string.IsNullOrEmpty(_config.ScimEndpoint))
                throw new InvalidOperationException("SCIM endpoint not configured");
            if (string.IsNullOrEmpty(_config.ScimToken))
                throw new InvalidOperationException("SCIM token not configured");
        }
        
        /// <summary>
        /// Initialize Google Directory API client
        /// </summary>
        private async Task<DirectoryService> InitializeGoogleDirectory(ILambdaContext context)
        {
            context.Logger.LogLine("Initializing Google Directory API client");
            
            // Parse service account JSON
            var serviceAccountCredential = GoogleCredential
                .FromJson(_config.GoogleServiceAccountKey)
                .CreateScoped(new[] { DirectoryService.Scope.AdminDirectoryGroupReadonly, DirectoryService.Scope.AdminDirectoryUserReadonly })
                .CreateWithUser($"admin@{_config.GoogleDomain}"); // Impersonate domain admin
            
            var service = new DirectoryService(new BaseClientService.Initializer
            {
                HttpClientInitializer = serviceAccountCredential,
                ApplicationName = "SCIM Sync Lambda"
            });
            
            return service;
        }
        
        /// <summary>
        /// Sync groups from Google Workspace to AWS Identity Center
        /// </summary>
        private async Task<int> SyncGroups(DirectoryService directoryService, ILambdaContext context)
        {
            context.Logger.LogLine("Starting group synchronization");
            
            var groupsRequest = directoryService.Groups.List();
            groupsRequest.Domain = _config.GoogleDomain;
            groupsRequest.MaxResults = 200;
            
            var groups = await groupsRequest.ExecuteAsync();
            var syncedCount = 0;
            
            if (groups.GroupsValue != null)
            {
                foreach (var group in groups.GroupsValue)
                {
                    // Apply group filter
                    if (!System.Text.RegularExpressions.Regex.IsMatch(group.Email, _config.GroupFilter))
                    {
                        context.Logger.LogLine($"Skipping group {group.Email} due to filter");
                        continue;
                    }
                    
                    // Create or update group in Identity Center
                    var scimGroup = new
                    {
                        schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
                        displayName = group.Name,
                        externalId = group.Id
                    };
                    
                    var success = await CreateOrUpdateScimResource($"Groups", scimGroup, group.Id, context);
                    if (success) syncedCount++;
                }
            }
            
            context.Logger.LogLine($"Synced {syncedCount} groups");
            return syncedCount;
        }
        
        /// <summary>
        /// Sync users from Google Workspace to AWS Identity Center
        /// </summary>
        private async Task<int> SyncUsers(DirectoryService directoryService, ILambdaContext context)
        {
            context.Logger.LogLine("Starting user synchronization");
            
            var usersRequest = directoryService.Users.List();
            usersRequest.Domain = _config.GoogleDomain;
            usersRequest.MaxResults = 500;
            
            var users = await usersRequest.ExecuteAsync();
            var syncedCount = 0;
            
            if (users.UsersValue != null)
            {
                foreach (var user in users.UsersValue)
                {
                    // Skip suspended users
                    if (user.Suspended == true)
                    {
                        context.Logger.LogLine($"Skipping suspended user {user.PrimaryEmail}");
                        continue;
                    }
                    
                    // Create or update user in Identity Center
                    var scimUser = new
                    {
                        schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
                        externalId = user.Id,
                        userName = user.PrimaryEmail,
                        name = new
                        {
                            givenName = user.Name?.GivenName ?? "",
                            familyName = user.Name?.FamilyName ?? ""
                        },
                        emails = new[]
                        {
                            new
                            {
                                value = user.PrimaryEmail,
                                type = "work",
                                primary = true
                            }
                        },
                        active = !user.Suspended
                    };
                    
                    var success = await CreateOrUpdateScimResource($"Users", scimUser, user.Id, context);
                    if (success) syncedCount++;
                }
            }
            
            context.Logger.LogLine($"Synced {syncedCount} users");
            return syncedCount;
        }
        
        /// <summary>
        /// Create or update a SCIM resource in AWS Identity Center
        /// </summary>
        private async Task<bool> CreateOrUpdateScimResource(string resourceType, object resource, string externalId, ILambdaContext context)
        {
            try
            {
                var json = JsonConvert.SerializeObject(resource);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                // Set authorization header
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ScimToken}");
                
                // Try to find existing resource by externalId
                var searchUrl = $"{_config.ScimEndpoint}/{resourceType}?filter=externalId eq \"{externalId}\"";
                var searchResponse = await _httpClient.GetAsync(searchUrl);
                
                if (searchResponse.IsSuccessStatusCode)
                {
                    var searchContent = await searchResponse.Content.ReadAsStringAsync();
                    dynamic searchResult = JsonConvert.DeserializeObject(searchContent);
                    
                    if (searchResult?.totalResults > 0)
                    {
                        // Update existing resource
                        var resourceId = searchResult.Resources[0].id;
                        var updateUrl = $"{_config.ScimEndpoint}/{resourceType}/{resourceId}";
                        var updateResponse = await _httpClient.PutAsync(updateUrl, content);
                        
                        if (updateResponse.IsSuccessStatusCode)
                        {
                            context.Logger.LogLine($"Updated {resourceType} with externalId: {externalId}");
                            return true;
                        }
                        else
                        {
                            context.Logger.LogLine($"Failed to update {resourceType}: {updateResponse.StatusCode}");
                            return false;
                        }
                    }
                    else
                    {
                        // Create new resource
                        var createUrl = $"{_config.ScimEndpoint}/{resourceType}";
                        var createResponse = await _httpClient.PostAsync(createUrl, content);
                        
                        if (createResponse.IsSuccessStatusCode)
                        {
                            context.Logger.LogLine($"Created {resourceType} with externalId: {externalId}");
                            return true;
                        }
                        else
                        {
                            context.Logger.LogLine($"Failed to create {resourceType}: {createResponse.StatusCode}");
                            return false;
                        }
                    }
                }
                else
                {
                    context.Logger.LogLine($"Failed to search {resourceType}: {searchResponse.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error syncing {resourceType} with externalId {externalId}: {ex.Message}");
                return false;
            }
        }
    }
    
    /// <summary>
    /// Request model for SCIM sync Lambda function
    /// </summary>
    public class ScimSyncRequest
    {
        public string Source { get; set; } = "manual";
        public string Action { get; set; } = "sync";
        public string? Environment { get; set; }
        public Dictionary<string, string>? AdditionalParameters { get; set; }
    }
    
    /// <summary>
    /// Response model for SCIM sync Lambda function
    /// </summary>
    public class ScimSyncResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SyncedUsers { get; set; }
        public int SyncedGroups { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Error { get; set; }
    }
    
    /// <summary>
    /// Configuration for SCIM sync
    /// </summary>
    public class ScimSyncConfig
    {
        public string GoogleDomain { get; set; } = string.Empty;
        public string GoogleServiceAccountKey { get; set; } = string.Empty;
        public string ScimEndpoint { get; set; } = string.Empty;
        public string ScimToken { get; set; } = string.Empty;
        public string GroupFilter { get; set; } = ".*";
        public bool SyncEnabled { get; set; } = true;
    }
}
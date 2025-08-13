using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK.AWS.ECS;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;


namespace AppInfraCdkV1.Apps.TrialMatch.Builders;

/// <summary>
/// Builder for creating ECS port mappings
/// </summary>
public class PortMappingBuilder
{
    /// <summary>
    /// Get port mappings for container
    /// </summary>
    public Amazon.CDK.AWS.ECS.PortMapping[] GetPortMappings(
        ContainerDefinitionConfig containerConfig,
        string containerName)
    {
        var portMappings = new List<Amazon.CDK.AWS.ECS.PortMapping>();

        if (containerConfig.PortMappings?.Count > 0)
        {
            foreach (var portMapping in containerConfig.PortMappings)
            {
                if (portMapping.ContainerPort == null) continue;

                portMappings.Add(new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = portMapping.ContainerPort.Value,
                    HostPort = portMapping.HostPort ?? portMapping.ContainerPort.Value,
                    Protocol = GetProtocol(portMapping.Protocol),
                    AppProtocol = GetAppProtocol(portMapping.AppProtocol),
                    Name = GeneratePortMappingName(containerName, portMapping.ContainerPort.Value, portMapping.Protocol ?? "tcp")
                });
            }
        }

        return portMappings.ToArray();
    }

    /// <summary>
    /// Generate port mapping name
    /// </summary>
    public string GeneratePortMappingName(string containerName, int containerPort, string protocol)
    {
        return $"{containerName}-{containerPort}-{protocol}";
    }

    /// <summary>
    /// Get protocol from string
    /// </summary>
    public Protocol GetProtocol(string? protocol)
    {
        return (protocol?.ToLower()) switch
        {
            "tcp" => Protocol.TCP,
            "udp" => Protocol.UDP,
            _ => Protocol.TCP
        };
    }

    /// <summary>
    /// Get application protocol from string with default
    /// </summary>
    public AppProtocol? GetAppProtocol(string? appProtocol)
    {
        if (string.IsNullOrWhiteSpace(appProtocol))
        {
            return AppProtocol.Http;
        }

        return appProtocol.ToLowerInvariant() switch
        {
            "http" => AppProtocol.Http,
            "https" => AppProtocol.Http,
            "grpc" => AppProtocol.Grpc,
            _ => AppProtocol.Http
        };
    }

    /// <summary>
    /// Create default port mappings for trial-match container
    /// </summary>
    public Amazon.CDK.AWS.ECS.PortMapping[] CreateDefaultPortMappings()
    {
        return new[]
        {
            new Amazon.CDK.AWS.ECS.PortMapping
            {
                ContainerPort = 8080,
                Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP,
                Name = "trial-match-8080-tcp"
            },
            new Amazon.CDK.AWS.ECS.PortMapping
            {
                ContainerPort = 80,
                Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP,
                Name = "trial-match-80-tcp"
            }
        };
    }

    /// <summary>
    /// Create placeholder container port mappings
    /// </summary>
    public Amazon.CDK.AWS.ECS.PortMapping[] CreatePlaceholderPortMappings()
    {
        return new[]
        {
            new Amazon.CDK.AWS.ECS.PortMapping
            {
                ContainerPort = 80,
                Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP,
                Name = "trial-match-placeholder-80-tcp"
            }
        };
    }
} 
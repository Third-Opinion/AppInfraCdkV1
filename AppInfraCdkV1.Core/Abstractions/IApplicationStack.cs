using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Core.Abstractions;

public interface IApplicationStack
{
    string ApplicationName { get; }
    void CreateResources(DeploymentContext context);
}
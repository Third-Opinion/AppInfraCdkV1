using Amazon.CDK;
using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Core.Abstractions;

public interface IStackFactory
{
    Stack CreateStack(App app, string stackName, DeploymentContext context);
}
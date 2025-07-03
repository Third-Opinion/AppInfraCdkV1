using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Core.Abstractions;

public interface IEnvironmentProvider
{
    EnvironmentConfig GetEnvironment(string environmentName);
    ApplicationConfig GetApplicationConfig(string appName, string environmentName);
}
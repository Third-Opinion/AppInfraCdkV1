using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.PublicThirdOpinion.Configuration
{
    public static class PublicThirdOpinionConfig
    {
        public static ApplicationConfig GetApplicationConfig()
        {
            return new ApplicationConfig
            {
                Name = "PublicThirdOpinion",
                Version = "1.0.0"
            };
        }
    }
}
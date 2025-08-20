// using AppInfraCdkV1.Core.Enums;
// using AppInfraCdkV1.Core.ExternalResources;
// using AppInfraCdkV1.Core.Models;
//
// namespace AppInfraCdkV1.Apps.TrialFinderV2;
//
// /// <summary>
// /// External resource dependencies for TrialFinderV2 application
// /// </summary>
// public class TrialFinderV2ExternalDependencies : ExternalResourceRequirements
// {
//     public override List<ExternalResourceRequirement> GetRequirements(DeploymentContext context)
//     {
//         var requirements = new List<ExternalResourceRequirement>
//         {
//             // ECS Task Role - required for ECS tasks to access AWS services
//             new ExternalResourceRequirement
//             {
//                 ResourceType = ExternalResourceType.IamRole,
//                 Purpose = IamPurpose.EcsTask,
//                 ExpectedName = context.Namer.IamRole(IamPurpose.EcsTask),
//                 ExpectedArn = context.Namer.IamRoleArn(IamPurpose.EcsTask),
//                 ValidationRules = new List<string> { "CanAssumeEcsTasks", "HasS3Access" },
//                 Description = "IAM role for ECS tasks to access application resources like S3 buckets",
//                 IsRequired = true,
//                 ExpectedTags = new Dictionary<string, string>
//                 {
//                     ["Environment"] = context.Environment.Name,
//                     ["Application"] = context.Application.Name,
//                     ["Purpose"] = "EcsTask"
//                 }
//             },
//
//             // ECS Execution Role - required for ECS to pull images and write logs
//             new ExternalResourceRequirement
//             {
//                 ResourceType = ExternalResourceType.IamRole,
//                 Purpose = IamPurpose.EcsExecution,
//                 ExpectedName = context.Namer.IamRole(IamPurpose.EcsExecution),
//                 ExpectedArn = context.Namer.IamRoleArn(IamPurpose.EcsExecution),
//                 ValidationRules = new List<string> { "CanAssumeEcsTasks", "HasEcsExecutionPolicy" },
//                 Description = "IAM role for ECS to pull container images and write logs to CloudWatch",
//                 IsRequired = true,
//                 ExpectedTags = new Dictionary<string, string>
//                 {
//                     ["Environment"] = context.Environment.Name,
//                     ["Application"] = context.Application.Name,
//                     ["Purpose"] = "EcsExecution"
//                 }
//             }
//         };
//
//         // Add production-specific requirements
//         if (context.Environment.IsProductionClass)
//         {
//             requirements.Add(new ExternalResourceRequirement
//             {
//                 ResourceType = ExternalResourceType.IamRole,
//                 Purpose = IamPurpose.S3Access,
//                 ExpectedName = context.Namer.IamRole(IamPurpose.S3Access),
//                 ExpectedArn = context.Namer.IamRoleArn(IamPurpose.S3Access),
//                 ValidationRules = new List<string> { "HasS3Access" },
//                 Description = "Dedicated S3 access role for production environments",
//                 IsRequired = true,
//                 EnvironmentSpecificRequirements = new Dictionary<string, object>
//                 {
//                     ["RequiresEncryption"] = true,
//                     ["RequiresVersioning"] = true
//                 }
//             });
//         }
//
//         return requirements;
//     }
// }
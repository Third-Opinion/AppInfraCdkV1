using Amazon.CDK.AWS.EC2;

namespace AppInfraCdkV1.Stacks.WebApp;

public record SecurityGroupBundle(
    ISecurityGroup AlbSg,
    ISecurityGroup EcsSg,
    ISecurityGroup DatabaseSg
);
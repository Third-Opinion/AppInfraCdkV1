using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace AppInfraCdkV1.PublicThirdOpinion.Lambda
{
    public class JwkGenerator
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IAmazonSecretsManager _secretsClient;
        private readonly string _bucketName;
        private readonly string _environment;
        private readonly string _secretsPrefix;

        public JwkGenerator()
        {
            _s3Client = new AmazonS3Client();
            _secretsClient = new AmazonSecretsManagerClient();
            _bucketName = Environment.GetEnvironmentVariable("BUCKET_NAME") ?? throw new InvalidOperationException("BUCKET_NAME not set");
            _environment = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? throw new InvalidOperationException("ENVIRONMENT not set");
            _secretsPrefix = Environment.GetEnvironmentVariable("SECRETS_PREFIX") ?? throw new InvalidOperationException("SECRETS_PREFIX not set");
        }

        public async Task<LambdaResponse> Handler(JwkRequest request, ILambdaContext context)
        {
            try
            {
                // Validate input
                if (string.IsNullOrEmpty(request.Name))
                {
                    return new LambdaResponse
                    {
                        StatusCode = 400,
                        Body = JsonSerializer.Serialize(new { error = "name parameter is required" })
                    };
                }

                if (string.IsNullOrEmpty(request.Algorithm) || (request.Algorithm != "ES384" && request.Algorithm != "RS384"))
                {
                    return new LambdaResponse
                    {
                        StatusCode = 400,
                        Body = JsonSerializer.Serialize(new { error = "algorithm parameter must be ES384 or RS384" })
                    };
                }

                var keyName = request.Name;
                var algorithm = request.Algorithm;
                var regenerate = request.Regenerate ?? false;

                // Check if key already exists
                var s3Key = $"jwks/{keyName}/jwks.json";
                var secretName = $"{_secretsPrefix}/{keyName}";

                if (!regenerate)
                {
                    try
                    {
                        await _s3Client.GetObjectMetadataAsync(_bucketName, s3Key);
                        return new LambdaResponse
                        {
                            StatusCode = 200,
                            Body = JsonSerializer.Serialize(new
                            {
                                message = $"Key {keyName} already exists. Set regenerate=true to overwrite.",
                                s3_path = $"s3://{_bucketName}/{s3Key}"
                            })
                        };
                    }
                    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // Key doesn't exist, proceed with generation
                    }
                }

                // Generate key pair
                var kid = Guid.NewGuid().ToString();
                JwkKey jwk;
                string privateKeyPem;

                if (algorithm == "ES384")
                {
                    using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
                    var parameters = ecdsa.ExportParameters(false);
                    
                    jwk = new JwkKey
                    {
                        Kty = "EC",
                        Use = "sig",
                        Alg = algorithm,
                        Kid = kid,
                        KeyOps = new[] { "verify" },
                        Ext = true,
                        Crv = "P-384",
                        X = Base64UrlEncode(parameters.Q.X),
                        Y = Base64UrlEncode(parameters.Q.Y)
                    };

                    // Export private key
                    var privateKey = ecdsa.ExportECPrivateKey();
                    privateKeyPem = ConvertToPem(privateKey, "EC PRIVATE KEY");
                }
                else // RS384
                {
                    using var rsa = RSA.Create(3072);
                    var parameters = rsa.ExportParameters(false);
                    
                    jwk = new JwkKey
                    {
                        Kty = "RSA",
                        Use = "sig",
                        Alg = algorithm,
                        Kid = kid,
                        KeyOps = new[] { "verify" },
                        Ext = true,
                        E = Base64UrlEncode(parameters.Exponent),
                        N = Base64UrlEncode(parameters.Modulus)
                    };

                    // Export private key
                    var privateKey = rsa.ExportRSAPrivateKey();
                    privateKeyPem = ConvertToPem(privateKey, "RSA PRIVATE KEY");
                }

                // Create JWKS document
                var jwks = new JwksDocument
                {
                    Keys = new[] { jwk }
                };

                // Store public key in S3
                var jwksJson = JsonSerializer.Serialize(jwks, new JsonSerializerOptions { WriteIndented = true });
                await _s3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = s3Key,
                    ContentBody = jwksJson,
                    ContentType = "application/json",
                    Metadata =
                    {
                        ["created"] = DateTime.UtcNow.ToString("O"),
                        ["algorithm"] = algorithm,
                        ["environment"] = _environment
                    }
                });

                // Store private key in Secrets Manager
                var secretValue = new
                {
                    private_key = privateKeyPem,
                    kid = kid,
                    algorithm = algorithm,
                    created = DateTime.UtcNow.ToString("O"),
                    public_s3_path = $"s3://{_bucketName}/{s3Key}"
                };

                try
                {
                    // Try to create the secret
                    await _secretsClient.CreateSecretAsync(new CreateSecretRequest
                    {
                        Name = secretName,
                        SecretString = JsonSerializer.Serialize(secretValue),
                        Description = $"Private key for JWK {keyName}",
                        Tags = new List<Amazon.SecretsManager.Model.Tag>
                        {
                            new Amazon.SecretsManager.Model.Tag { Key = "Environment", Value = _environment },
                            new Amazon.SecretsManager.Model.Tag { Key = "KeyName", Value = keyName },
                            new Amazon.SecretsManager.Model.Tag { Key = "Algorithm", Value = algorithm }
                        }
                    });
                }
                catch (ResourceExistsException)
                {
                    // Update existing secret if regenerating
                    if (regenerate)
                    {
                        await _secretsClient.PutSecretValueAsync(new PutSecretValueRequest
                        {
                            SecretId = secretName,
                            SecretString = JsonSerializer.Serialize(secretValue)
                        });
                    }
                }

                return new LambdaResponse
                {
                    StatusCode = 200,
                    Body = JsonSerializer.Serialize(new
                    {
                        message = $"Successfully generated JWK for {keyName}",
                        kid = kid,
                        algorithm = algorithm,
                        s3_path = $"s3://{_bucketName}/{s3Key}",
                        secret_name = secretName
                    })
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error generating JWK: {ex.Message}");
                return new LambdaResponse
                {
                    StatusCode = 500,
                    Body = JsonSerializer.Serialize(new { error = $"Internal error: {ex.Message}" })
                };
            }
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string ConvertToPem(byte[] keyData, string keyType)
        {
            var base64 = Convert.ToBase64String(keyData);
            var sb = new StringBuilder();
            sb.AppendLine($"-----BEGIN {keyType}-----");
            
            for (int i = 0; i < base64.Length; i += 64)
            {
                sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
            }
            
            sb.AppendLine($"-----END {keyType}-----");
            return sb.ToString();
        }
    }

    public class JwkRequest
    {
        public string Name { get; set; }
        public string Algorithm { get; set; }
        public bool? Regenerate { get; set; }
    }

    public class LambdaResponse
    {
        public int StatusCode { get; set; }
        public string Body { get; set; }
    }

    public class JwkKey
    {
        public string Kty { get; set; }
        public string Use { get; set; }
        public string Alg { get; set; }
        public string Kid { get; set; }
        public string[] KeyOps { get; set; }
        public bool Ext { get; set; }
        
        // EC specific
        public string Crv { get; set; }
        public string X { get; set; }
        public string Y { get; set; }
        
        // RSA specific
        public string E { get; set; }
        public string N { get; set; }
    }

    public class JwksDocument
    {
        public JwkKey[] Keys { get; set; }
    }
}
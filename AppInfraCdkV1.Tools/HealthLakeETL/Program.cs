using System;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.Glue;
using CommandLine;

namespace AppInfraCdkV1.Tools.HealthLakeETL
{
    /// <summary>
    /// Console application for running HealthLake ETL processing
    /// Can be used as a Glue job or standalone batch process
    /// </summary>
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await Parser.Default.ParseArguments<CommandLineOptions>(args)
                .MapResult(
                    async (CommandLineOptions opts) => await RunETL(opts),
                    errs => Task.FromResult(1));
        }
        
        private static async Task<int> RunETL(CommandLineOptions options)
        {
            try
            {
                Console.WriteLine("Starting HealthLake ETL Processing");
                Console.WriteLine($"Export Path: {options.ExportPath}");
                Console.WriteLine($"Raw Bucket: {options.RawBucket}");
                Console.WriteLine($"Curated Bucket: {options.CuratedBucket}");
                Console.WriteLine($"Multi-tenant: {options.EnableMultiTenant}");
                
                var config = new HealthLakeETLConfig
                {
                    RawBucket = options.RawBucket,
                    CuratedBucket = options.CuratedBucket,
                    TenantPartitionKey = options.TenantPartitionKey,
                    EnableMultiTenant = options.EnableMultiTenant,
                    Timestamp = options.Timestamp ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                };
                
                using var s3Client = new AmazonS3Client();
                using var glueClient = new AmazonGlueClient();
                
                var processor = new HealthLakeETLProcessor(s3Client, glueClient, config);
                await processor.ProcessExportAsync(options.ExportPath, config.Timestamp);
                
                Console.WriteLine("ETL Processing completed successfully");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ETL Processing failed: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }
    }
    
    /// <summary>
    /// Command line options for the ETL processor
    /// </summary>
    public class CommandLineOptions
    {
        [Option('e', "export-path", Required = true, HelpText = "S3 path containing HealthLake export files")]
        public string ExportPath { get; set; } = string.Empty;
        
        [Option('r', "raw-bucket", Required = true, HelpText = "S3 bucket for raw data")]
        public string RawBucket { get; set; } = string.Empty;
        
        [Option('c', "curated-bucket", Required = true, HelpText = "S3 bucket for curated data")]
        public string CuratedBucket { get; set; } = string.Empty;
        
        [Option('t', "tenant-key", Default = "tenantGuid", HelpText = "Partition key for tenant data")]
        public string TenantPartitionKey { get; set; } = "tenantGuid";
        
        [Option('m', "multi-tenant", Default = true, HelpText = "Enable multi-tenant processing")]
        public bool EnableMultiTenant { get; set; } = true;
        
        [Option('s', "timestamp", HelpText = "Export timestamp (defaults to current time)")]
        public string? Timestamp { get; set; }
        
        [Option('j', "job-id", HelpText = "HealthLake export job ID for tracking")]
        public string? ExportJobId { get; set; }
    }
}
#!/usr/bin/env python3
"""
Glue ETL Script for processing HealthLake FHIR exports with multi-tenant support.
This script reads NDJSON files exported from HealthLake, extracts tenant information
from security labels, and partitions data by tenant GUID in S3.
"""

import sys
import json
from datetime import datetime
from awsglue.transforms import *
from awsglue.utils import getResolvedOptions
from pyspark.context import SparkContext
from awsglue.context import GlueContext
from awsglue.job import Job
from pyspark.sql import SparkSession
from pyspark.sql.functions import col, lit, udf, when, explode, first
from pyspark.sql.types import StringType, StructType, StructField, ArrayType

# Get job parameters
args = getResolvedOptions(sys.argv, [
    'JOB_NAME',
    'RAW_BUCKET',
    'CURATED_BUCKET',
    'TENANT_PARTITION_KEY',
    'ENABLE_MULTI_TENANT',
    'EXPORT_PATH',
    'TIMESTAMP'
])

sc = SparkContext()
glueContext = GlueContext(sc)
spark = glueContext.spark_session
job = Job(glueContext)
job.init(args['JOB_NAME'], args)

# Configuration
raw_bucket = args['RAW_BUCKET']
curated_bucket = args['CURATED_BUCKET']
tenant_partition_key = args['TENANT_PARTITION_KEY']
enable_multi_tenant = args['ENABLE_MULTI_TENANT'].lower() == 'true'
export_path = args['EXPORT_PATH']
timestamp = args['TIMESTAMP']

# Tenant claim system identifier
TENANT_CLAIM_SYSTEM = "http://thirdopinion.io/identity/claims/tenant"

def extract_tenant_id(security_labels):
    """
    Extract tenant ID from FHIR security labels.
    Expected format:
    "security": [
        {
            "system": "http://thirdopinion.io/identity/claims/tenant",
            "code": "11111111-1111-1111-1111-111111111111"
        }
    ]
    """
    if not security_labels:
        return "unknown"
    
    try:
        for label in security_labels:
            if isinstance(label, dict) and label.get('system') == TENANT_CLAIM_SYSTEM:
                return label.get('code', 'unknown')
    except:
        pass
    
    return "unknown"

# Register UDF for tenant extraction
extract_tenant_udf = udf(extract_tenant_id, StringType())

def process_resource_type(resource_type, input_path, output_base_path):
    """
    Process a specific FHIR resource type and partition by tenant.
    """
    print(f"Processing {resource_type} from {input_path}")
    
    try:
        # Read NDJSON files
        df = spark.read.json(input_path)
        
        if df.count() == 0:
            print(f"No data found for {resource_type}")
            return
        
        # Extract tenant ID from security labels
        if enable_multi_tenant:
            df_with_tenant = df.withColumn(
                tenant_partition_key,
                extract_tenant_udf(col("meta.security"))
            )
        else:
            # Single tenant mode - use default tenant
            df_with_tenant = df.withColumn(tenant_partition_key, lit("default"))
        
        # Add processing metadata
        df_with_metadata = df_with_tenant \
            .withColumn("_export_timestamp", lit(timestamp)) \
            .withColumn("_processing_date", lit(datetime.now().isoformat()))
        
        # Write partitioned data to S3
        output_path = f"s3://{curated_bucket}/{resource_type.lower()}/"
        
        df_with_metadata.write \
            .mode("append") \
            .partitionBy(tenant_partition_key, "_export_timestamp") \
            .parquet(output_path)
        
        print(f"Successfully wrote {resource_type} to {output_path}")
        
        # Create Glue catalog table if it doesn't exist
        create_glue_table(resource_type.lower(), output_path, df_with_metadata.schema)
        
    except Exception as e:
        print(f"Error processing {resource_type}: {str(e)}")
        raise

def create_glue_table(table_name, location, schema):
    """
    Create or update Glue catalog table for the processed data.
    """
    try:
        database_name = "healthlake_analytics"
        
        # Convert Spark schema to Glue table schema
        glue_schema = []
        for field in schema.fields:
            if field.name not in [tenant_partition_key, "_export_timestamp"]:
                glue_schema.append({
                    'Name': field.name,
                    'Type': str(field.dataType).lower()
                })
        
        # Define partition keys
        partition_keys = [
            {'Name': tenant_partition_key, 'Type': 'string'},
            {'Name': '_export_timestamp', 'Type': 'string'}
        ]
        
        # Create table using Glue Data Catalog API
        glue_client = boto3.client('glue')
        
        try:
            glue_client.create_table(
                DatabaseName=database_name,
                TableInput={
                    'Name': table_name,
                    'StorageDescriptor': {
                        'Columns': glue_schema,
                        'Location': location,
                        'InputFormat': 'org.apache.hadoop.mapred.TextInputFormat',
                        'OutputFormat': 'org.apache.hadoop.hive.ql.io.HiveIgnoreKeyTextOutputFormat',
                        'SerdeInfo': {
                            'SerializationLibrary': 'org.apache.hadoop.hive.serde2.lazy.LazySimpleSerDe'
                        },
                        'StoredAsSubDirectories': False
                    },
                    'PartitionKeys': partition_keys,
                    'TableType': 'EXTERNAL_TABLE',
                    'Parameters': {
                        'classification': 'parquet',
                        'compressionType': 'snappy',
                        'typeOfData': 'file'
                    }
                }
            )
            print(f"Created Glue table: {table_name}")
        except glue_client.exceptions.AlreadyExistsException:
            print(f"Table {table_name} already exists, updating...")
            glue_client.update_table(
                DatabaseName=database_name,
                TableInput={
                    'Name': table_name,
                    'StorageDescriptor': {
                        'Columns': glue_schema,
                        'Location': location
                    },
                    'PartitionKeys': partition_keys
                }
            )
            
    except Exception as e:
        print(f"Error creating/updating Glue table {table_name}: {str(e)}")

# Main processing logic
def main():
    """
    Main ETL processing function.
    """
    print(f"Starting HealthLake ETL processing")
    print(f"Export path: {export_path}")
    print(f"Multi-tenant mode: {enable_multi_tenant}")
    
    # List of FHIR resource types to process
    resource_types = [
        "Patient",
        "Observation", 
        "Condition",
        "MedicationRequest",
        "Procedure",
        "DiagnosticReport",
        "Encounter",
        "AllergyIntolerance",
        "Immunization",
        "CarePlan",
        "Goal",
        "MedicationStatement"
    ]
    
    # Process each resource type
    for resource_type in resource_types:
        input_path = f"{export_path}{resource_type}*.ndjson"
        output_base_path = f"s3://{curated_bucket}/"
        
        try:
            process_resource_type(resource_type, input_path, output_base_path)
        except Exception as e:
            print(f"Failed to process {resource_type}: {str(e)}")
            # Continue with other resource types even if one fails
            continue
    
    print("ETL processing completed successfully")

# Import boto3 for Glue catalog operations
import boto3

# Run main function
if __name__ == "__main__":
    main()
    job.commit()
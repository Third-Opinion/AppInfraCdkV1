-- test-athena-queries.sql
-- Sample Athena queries to test FHIR data imported into HealthLake via Lake Formation
-- These queries validate the end-to-end data pipeline: Synthea -> S3 -> HealthLake -> Lake Formation -> Athena

-- =============================================================================
-- 1. BASIC CONNECTIVITY AND DATABASE STRUCTURE
-- =============================================================================

-- List all databases in the catalog
SHOW DATABASES;

-- Use the tenant-specific database (replace with actual tenant ID and environment)
-- Format: fhir_raw_{tenant_id_with_underscores}_{environment}
USE fhir_raw_10000000_0000_0000_0000_000000000001_development;

-- List all tables in the database
SHOW TABLES;

-- Get schema information for key tables
DESCRIBE patient;
DESCRIBE encounter;
DESCRIBE observation;

-- =============================================================================
-- 2. PATIENT DATA QUERIES
-- =============================================================================

-- Count total patients
SELECT COUNT(*) as total_patients
FROM patient;

-- Sample patient records
SELECT 
    id,
    name_family,
    name_given,
    gender,
    birth_date,
    address_city,
    address_state
FROM patient 
LIMIT 10;

-- Patient demographics summary
SELECT 
    gender,
    COUNT(*) as count,
    ROUND(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM patient), 2) as percentage
FROM patient 
WHERE gender IS NOT NULL
GROUP BY gender
ORDER BY count DESC;

-- Age distribution (assuming birth_date is available)
SELECT 
    CASE 
        WHEN age_years < 18 THEN 'Under 18'
        WHEN age_years BETWEEN 18 AND 30 THEN '18-30'
        WHEN age_years BETWEEN 31 AND 50 THEN '31-50'
        WHEN age_years BETWEEN 51 AND 70 THEN '51-70'
        ELSE 'Over 70'
    END as age_group,
    COUNT(*) as count
FROM (
    SELECT 
        id,
        DATE_DIFF('year', DATE(birth_date), CURRENT_DATE) as age_years
    FROM patient
    WHERE birth_date IS NOT NULL
) age_calc
GROUP BY 
    CASE 
        WHEN age_years < 18 THEN 'Under 18'
        WHEN age_years BETWEEN 18 AND 30 THEN '18-30'
        WHEN age_years BETWEEN 31 AND 50 THEN '31-50'
        WHEN age_years BETWEEN 51 AND 70 THEN '51-70'
        ELSE 'Over 70'
    END
ORDER BY count DESC;

-- Geographic distribution
SELECT 
    address_state,
    address_city,
    COUNT(*) as patient_count
FROM patient 
WHERE address_state IS NOT NULL
GROUP BY address_state, address_city
ORDER BY patient_count DESC
LIMIT 20;

-- =============================================================================
-- 3. ENCOUNTER DATA QUERIES
-- =============================================================================

-- Count total encounters
SELECT COUNT(*) as total_encounters
FROM encounter;

-- Encounter types distribution
SELECT 
    encounter_class,
    encounter_type,
    COUNT(*) as count
FROM encounter
GROUP BY encounter_class, encounter_type
ORDER BY count DESC
LIMIT 20;

-- Recent encounters (last 30 days from latest date in dataset)
WITH latest_date AS (
    SELECT MAX(DATE(start_date)) as max_date
    FROM encounter
    WHERE start_date IS NOT NULL
)
SELECT 
    e.id,
    e.patient_id,
    p.name_family,
    p.name_given,
    e.encounter_class,
    e.encounter_type,
    e.start_date,
    e.end_date
FROM encounter e
JOIN patient p ON e.patient_id = p.id
CROSS JOIN latest_date ld
WHERE DATE(e.start_date) >= DATE_ADD('day', -30, ld.max_date)
ORDER BY e.start_date DESC
LIMIT 50;

-- Average encounter duration by type
SELECT 
    encounter_type,
    encounter_class,
    COUNT(*) as encounter_count,
    AVG(DATE_DIFF('hour', 
        CAST(start_date AS TIMESTAMP), 
        CAST(end_date AS TIMESTAMP)
    )) as avg_duration_hours
FROM encounter
WHERE start_date IS NOT NULL 
    AND end_date IS NOT NULL
    AND encounter_type IS NOT NULL
GROUP BY encounter_type, encounter_class
HAVING encounter_count >= 5
ORDER BY avg_duration_hours DESC;

-- =============================================================================
-- 4. OBSERVATION DATA QUERIES
-- =============================================================================

-- Count observations by category
SELECT 
    category,
    COUNT(*) as observation_count
FROM observation
WHERE category IS NOT NULL
GROUP BY category
ORDER BY observation_count DESC;

-- Most common observation codes
SELECT 
    code_coding_code,
    code_coding_display,
    COUNT(*) as frequency
FROM observation
WHERE code_coding_code IS NOT NULL
GROUP BY code_coding_code, code_coding_display
ORDER BY frequency DESC
LIMIT 20;

-- Vital signs observations (common LOINC codes)
SELECT 
    o.patient_id,
    p.name_family,
    p.name_given,
    o.code_coding_display as observation_type,
    o.value_quantity_value,
    o.value_quantity_unit,
    o.effective_date_time
FROM observation o
JOIN patient p ON o.patient_id = p.id
WHERE o.code_coding_code IN (
    '8480-6',  -- Systolic BP
    '8462-4',  -- Diastolic BP
    '8867-4',  -- Heart rate
    '9279-1',  -- Respiratory rate
    '8310-5',  -- Body temperature
    '29463-7', -- Body weight
    '8302-2'   -- Body height
)
    AND o.value_quantity_value IS NOT NULL
ORDER BY o.effective_date_time DESC
LIMIT 100;

-- =============================================================================
-- 5. CONDITION DATA QUERIES
-- =============================================================================

-- Most common conditions
SELECT 
    code_coding_code,
    code_coding_display as condition_name,
    COUNT(*) as frequency
FROM condition
WHERE code_coding_code IS NOT NULL
GROUP BY code_coding_code, code_coding_display
ORDER BY frequency DESC
LIMIT 20;

-- Active conditions by patient
SELECT 
    c.patient_id,
    p.name_family,
    p.name_given,
    COUNT(*) as active_conditions
FROM condition c
JOIN patient p ON c.patient_id = p.id
WHERE c.clinical_status = 'active'
GROUP BY c.patient_id, p.name_family, p.name_given
HAVING active_conditions > 2
ORDER BY active_conditions DESC
LIMIT 20;

-- =============================================================================
-- 6. MEDICATION DATA QUERIES
-- =============================================================================

-- Most prescribed medications
SELECT 
    m.code_coding_display as medication_name,
    COUNT(*) as prescription_count
FROM medication_request mr
JOIN medication m ON mr.medication_reference = CONCAT('Medication/', m.id)
WHERE m.code_coding_display IS NOT NULL
GROUP BY m.code_coding_display
ORDER BY prescription_count DESC
LIMIT 20;

-- Active medications by patient
SELECT 
    mr.patient_id,
    p.name_family,
    p.name_given,
    COUNT(*) as active_medications
FROM medication_request mr
JOIN patient p ON mr.patient_id = p.id
WHERE mr.status = 'active'
GROUP BY mr.patient_id, p.name_family, p.name_given
HAVING active_medications > 1
ORDER BY active_medications DESC;

-- =============================================================================
-- 7. CROSS-RESOURCE ANALYTICS
-- =============================================================================

-- Patient care summary (encounters, conditions, medications)
SELECT 
    p.id as patient_id,
    p.name_family,
    p.name_given,
    p.gender,
    DATE_DIFF('year', DATE(p.birth_date), CURRENT_DATE) as age,
    COUNT(DISTINCT e.id) as total_encounters,
    COUNT(DISTINCT c.id) as total_conditions,
    COUNT(DISTINCT mr.id) as total_medications,
    COUNT(DISTINCT o.id) as total_observations
FROM patient p
LEFT JOIN encounter e ON p.id = e.patient_id
LEFT JOIN condition c ON p.id = c.patient_id
LEFT JOIN medication_request mr ON p.id = mr.patient_id
LEFT JOIN observation o ON p.id = o.patient_id
GROUP BY p.id, p.name_family, p.name_given, p.gender, p.birth_date
HAVING total_encounters > 0
ORDER BY total_encounters DESC
LIMIT 25;

-- Healthcare utilization patterns
SELECT 
    e.encounter_class,
    COUNT(DISTINCT e.patient_id) as unique_patients,
    COUNT(e.id) as total_encounters,
    ROUND(COUNT(e.id) * 1.0 / COUNT(DISTINCT e.patient_id), 2) as avg_encounters_per_patient
FROM encounter e
WHERE e.encounter_class IS NOT NULL
GROUP BY e.encounter_class
ORDER BY total_encounters DESC;

-- Monthly encounter trends
SELECT 
    DATE_TRUNC('month', DATE(start_date)) as encounter_month,
    COUNT(*) as encounter_count,
    COUNT(DISTINCT patient_id) as unique_patients
FROM encounter
WHERE start_date IS NOT NULL
    AND DATE(start_date) >= DATE_ADD('month', -12, CURRENT_DATE)
GROUP BY DATE_TRUNC('month', DATE(start_date))
ORDER BY encounter_month;

-- =============================================================================
-- 8. DATA QUALITY CHECKS
-- =============================================================================

-- Check for required fields completeness
SELECT 
    'patient' as table_name,
    COUNT(*) as total_records,
    SUM(CASE WHEN id IS NULL THEN 1 ELSE 0 END) as missing_id,
    SUM(CASE WHEN name_family IS NULL THEN 1 ELSE 0 END) as missing_family_name,
    SUM(CASE WHEN gender IS NULL THEN 1 ELSE 0 END) as missing_gender,
    SUM(CASE WHEN birth_date IS NULL THEN 1 ELSE 0 END) as missing_birth_date
FROM patient

UNION ALL

SELECT 
    'encounter' as table_name,
    COUNT(*) as total_records,
    SUM(CASE WHEN id IS NULL THEN 1 ELSE 0 END) as missing_id,
    SUM(CASE WHEN patient_id IS NULL THEN 1 ELSE 0 END) as missing_patient_id,
    SUM(CASE WHEN start_date IS NULL THEN 1 ELSE 0 END) as missing_start_date,
    SUM(CASE WHEN encounter_class IS NULL THEN 1 ELSE 0 END) as missing_class
FROM encounter

UNION ALL

SELECT 
    'observation' as table_name,
    COUNT(*) as total_records,
    SUM(CASE WHEN id IS NULL THEN 1 ELSE 0 END) as missing_id,
    SUM(CASE WHEN patient_id IS NULL THEN 1 ELSE 0 END) as missing_patient_id,
    SUM(CASE WHEN code_coding_code IS NULL THEN 1 ELSE 0 END) as missing_code,
    SUM(CASE WHEN effective_date_time IS NULL THEN 1 ELSE 0 END) as missing_date
FROM observation;

-- Check referential integrity
SELECT 
    'encounter_to_patient' as relationship,
    COUNT(*) as total_encounters,
    COUNT(p.id) as encounters_with_valid_patient,
    COUNT(*) - COUNT(p.id) as orphaned_encounters
FROM encounter e
LEFT JOIN patient p ON e.patient_id = p.id

UNION ALL

SELECT 
    'observation_to_patient' as relationship,
    COUNT(*) as total_observations,
    COUNT(p.id) as observations_with_valid_patient,
    COUNT(*) - COUNT(p.id) as orphaned_observations
FROM observation o
LEFT JOIN patient p ON o.patient_id = p.id;

-- =============================================================================
-- 9. PERFORMANCE TEST QUERIES
-- =============================================================================

-- Large aggregation query (test performance)
SELECT 
    DATE_TRUNC('week', DATE(effective_date_time)) as observation_week,
    code_coding_code,
    code_coding_display,
    COUNT(*) as observation_count,
    AVG(CAST(value_quantity_value AS DOUBLE)) as avg_value,
    MIN(CAST(value_quantity_value AS DOUBLE)) as min_value,
    MAX(CAST(value_quantity_value AS DOUBLE)) as max_value
FROM observation
WHERE effective_date_time IS NOT NULL
    AND value_quantity_value IS NOT NULL
    AND TRY_CAST(value_quantity_value AS DOUBLE) IS NOT NULL
GROUP BY 
    DATE_TRUNC('week', DATE(effective_date_time)),
    code_coding_code,
    code_coding_display
HAVING observation_count >= 5
ORDER BY observation_week DESC, observation_count DESC;

-- =============================================================================
-- 10. TENANT ISOLATION VERIFICATION
-- =============================================================================

-- Verify all data belongs to expected tenant
-- (This query structure will depend on how tenant isolation is implemented)
SELECT 
    'Data should only contain tenant: 10000000-0000-0000-0000-000000000001' as note,
    COUNT(DISTINCT 
        CASE 
            WHEN tenant_id IS NOT NULL AND tenant_id != '10000000-0000-0000-0000-000000000001' 
            THEN tenant_id 
        END
    ) as unexpected_tenant_count
FROM patient
WHERE tenant_id IS NOT NULL;

-- End of queries
-- =============================================================================
-- NOTES:
-- 1. Replace database name with actual tenant-specific database
-- 2. Column names may vary based on HealthLake FHIR mapping
-- 3. Adjust date filters based on your data generation timeframe
-- 4. Some queries may need modification based on actual table schema
-- 5. Test with small LIMIT first, then remove for full results
-- =============================================================================
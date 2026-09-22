-- 20260911_registration_3nf_verify.sql
-- Verification queries for 3NF registration backfill.

-- 1. Registrations without PatientRefID
SELECT "RecordID", "DivisionID", "RecordCode", "PatientCode", "FullName"
FROM "HEX_ExamRecord"
WHERE "PatientRefID" IS NULL;

-- 2. Child FK rows from another tenant (cross-tenant joins)
SELECT 'ExamRecord->Patient' AS "CrossTenantJoin", r."RecordID", r."DivisionID" AS "RecordDivision", p."PatientRefID", p."DivisionID" AS "TargetDivision"
FROM "HEX_ExamRecord" r
JOIN "HEX_Patient" p ON r."PatientRefID" = p."PatientRefID"
WHERE r."DivisionID" <> p."DivisionID"
UNION ALL
SELECT 'ExamRecord->Insurance', r."RecordID", r."DivisionID", pi."InsuranceRefID", pi."DivisionID"
FROM "HEX_ExamRecord" r
JOIN "HEX_PatientInsurance" pi ON r."InsuranceRefID" = pi."InsuranceRefID"
WHERE r."DivisionID" <> pi."DivisionID"
UNION ALL
SELECT 'ExamRecord->Employment', r."RecordID", r."DivisionID", pe."EmploymentRefID", pe."DivisionID"
FROM "HEX_ExamRecord" r
JOIN "HEX_PatientEmployment" pe ON r."EmploymentRefID" = pe."EmploymentRefID"
WHERE r."DivisionID" <> pe."DivisionID"
UNION ALL
SELECT 'ExamRecord->Relative', r."RecordID", r."DivisionID", pr."RelativeRefID", pr."DivisionID"
FROM "HEX_ExamRecord" r
JOIN "HEX_PatientRelative" pr ON r."RelativeRefID" = pr."RelativeRefID"
WHERE r."DivisionID" <> pr."DivisionID";

-- 3. Duplicate (DivisionID, HisPatientID) for positive IDs
SELECT "DivisionID", "HisPatientID", COUNT(*) AS "DuplicateCount"
FROM "HEX_Patient"
WHERE "HisPatientID" IS NOT NULL AND "HisPatientID" > 0
GROUP BY "DivisionID", "HisPatientID"
HAVING COUNT(*) > 1;

-- 4. Invalid option category/FK pairs
SELECT 'Patient.IdentityIssuer' AS "Source", p."PatientRefID" AS "EntityID", o."Category"
FROM "HEX_Patient" p
JOIN "HEX_MasterDataOption" o ON p."IdentityIssuerOptionID" = o."OptionID"
WHERE o."Category" <> 'IDENTITY_ISSUER' OR p."DivisionID" <> o."DivisionID"
UNION ALL
SELECT 'Patient.Ethnicity', p."PatientRefID", o."Category"
FROM "HEX_Patient" p
JOIN "HEX_MasterDataOption" o ON p."EthnicityOptionID" = o."OptionID"
WHERE o."Category" <> 'ETHNICITY' OR p."DivisionID" <> o."DivisionID"
UNION ALL
SELECT 'PatientInsurance.InsuranceObject', pi."InsuranceRefID", o."Category"
FROM "HEX_PatientInsurance" pi
JOIN "HEX_MasterDataOption" o ON pi."InsuranceObjectOptionID" = o."OptionID"
WHERE o."Category" <> 'INSURANCE_OBJECT' OR pi."DivisionID" <> o."DivisionID"
UNION ALL
SELECT 'PatientInsurance.RegistrationPlace', pi."InsuranceRefID", o."Category"
FROM "HEX_PatientInsurance" pi
JOIN "HEX_MasterDataOption" o ON pi."RegistrationPlaceOptionID" = o."OptionID"
WHERE o."Category" <> 'REGISTRATION_PLACE' OR pi."DivisionID" <> o."DivisionID"
UNION ALL
SELECT 'PatientEmployment.Occupation', pe."EmploymentRefID", o."Category"
FROM "HEX_PatientEmployment" pe
JOIN "HEX_MasterDataOption" o ON pe."OccupationOptionID" = o."OptionID"
WHERE o."Category" <> 'OCCUPATION' OR pe."DivisionID" <> o."DivisionID"
UNION ALL
SELECT 'PatientRelative.Relationship', pr."RelativeRefID", o."Category"
FROM "HEX_PatientRelative" pr
JOIN "HEX_MasterDataOption" o ON pr."RelationshipOptionID" = o."OptionID"
WHERE o."Category" <> 'RELATIONSHIP' OR pr."DivisionID" <> o."DivisionID"
UNION ALL
SELECT 'ExamRecord.PatientType', r."RecordID", o."Category"
FROM "HEX_ExamRecord" r
JOIN "HEX_MasterDataOption" o ON r."PatientTypeOptionID" = o."OptionID"
WHERE o."Category" NOT IN ('PATIENT_TYPE', 'PATIENT_SUBJECT') OR r."DivisionID" <> o."DivisionID"
UNION ALL
SELECT 'ExamRecord.PaymentSource', r."RecordID", o."Category"
FROM "HEX_ExamRecord" r
JOIN "HEX_MasterDataOption" o ON r."PaymentSourceOptionID" = o."OptionID"
WHERE o."Category" <> 'PAYMENT_SOURCE' OR r."DivisionID" <> o."DivisionID"
UNION ALL
SELECT 'ExamRecord.ExamLocation', r."RecordID", o."Category"
FROM "HEX_ExamRecord" r
JOIN "HEX_MasterDataOption" o ON r."ExamLocationOptionID" = o."OptionID"
WHERE o."Category" <> 'EXAM_LOCATION' OR r."DivisionID" <> o."DivisionID";

-- 5. Normalized vs Legacy projection mismatches
SELECT
    r."RecordID",
    r."DivisionID",
    r."FullName" AS "LegacyFullName",
    p."FullName" AS "NormalizedFullName",
    r."PatientCode" AS "LegacyPatientCode",
    p."PatientCode" AS "NormalizedPatientCode"
FROM "HEX_ExamRecord" r
JOIN "HEX_Patient" p ON r."PatientRefID" = p."PatientRefID"
WHERE r."FullName" <> p."FullName"
   OR (r."PatientCode" <> '' AND p."PatientCode" <> '' AND r."PatientCode" <> p."PatientCode");

-- 6. Counts before and after migration
SELECT
    (SELECT COUNT(*) FROM "HEX_ExamRecord") AS "TotalExamRecords",
    (SELECT COUNT(*) FROM "HEX_ExamRecord" WHERE "PatientRefID" IS NOT NULL) AS "LinkedExamRecords",
    (SELECT COUNT(*) FROM "HEX_Patient") AS "TotalPatients",
    (SELECT COUNT(*) FROM "HEX_PatientInsurance") AS "TotalInsurances",
    (SELECT COUNT(*) FROM "HEX_PatientEmployment") AS "TotalEmployments",
    (SELECT COUNT(*) FROM "HEX_PatientRelative") AS "TotalRelatives",
    (SELECT COUNT(*) FROM "HEX_Registration3NfConflictReport") AS "TotalConflictsReported";

-- 20260911_registration_3nf_backfill.sql
-- Idempotent 3NF registration backfill script for PostgreSQL.
-- Safe to rerun; wrapped in transactions; does not delete legacy values.

-- ============================================================================
-- Phase 0: Ensure conflict tracking table exists
-- ============================================================================
CREATE TABLE IF NOT EXISTS "HEX_Registration3NfConflictReport" (
    "ReportID" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "RecordID" uuid NOT NULL,
    "DivisionID" character varying(20) NOT NULL,
    "FieldCategory" character varying(50) NOT NULL,
    "InvalidCode" character varying(50) NOT NULL,
    "ReportedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_HEX_Registration3NfConflictReport" PRIMARY KEY ("ReportID")
);

-- ============================================================================
-- Phase 1: Backfill Master Data Option references on HEX_ExamRecord
-- ============================================================================
UPDATE "HEX_ExamRecord" r
SET "PatientTypeOptionID" = (
    SELECT o."OptionID"
    FROM "HEX_MasterDataOption" o
    WHERE o."DivisionID" = r."DivisionID"
      AND o."Category" IN ('PATIENT_SUBJECT', 'PATIENT_TYPE')
      AND o."Code" = r."PatientTypeCode"
    ORDER BY CASE WHEN o."Category" = 'PATIENT_SUBJECT' THEN 0 ELSE 1 END
    LIMIT 1
)
WHERE r."PatientTypeOptionID" IS NULL
  AND r."PatientTypeCode" IS NOT NULL
  AND r."PatientTypeCode" <> ''
  AND EXISTS (
      SELECT 1
      FROM "HEX_MasterDataOption" o
      WHERE o."DivisionID" = r."DivisionID"
        AND o."Category" IN ('PATIENT_SUBJECT', 'PATIENT_TYPE')
        AND o."Code" = r."PatientTypeCode"
  );

UPDATE "HEX_ExamRecord" r
SET "PaymentSourceOptionID" = o."OptionID"
FROM "HEX_MasterDataOption" o
WHERE r."PaymentSourceOptionID" IS NULL
  AND r."PaymentSourceCode" IS NOT NULL
  AND r."PaymentSourceCode" <> ''
  AND o."DivisionID" = r."DivisionID"
  AND o."Category" = 'PAYMENT_SOURCE'
  AND o."Code" = r."PaymentSourceCode";

UPDATE "HEX_ExamRecord" r
SET "ExamLocationOptionID" = o."OptionID"
FROM "HEX_MasterDataOption" o
WHERE r."ExamLocationOptionID" IS NULL
  AND r."ExamLocationCode" IS NOT NULL
  AND r."ExamLocationCode" <> ''
  AND o."DivisionID" = r."DivisionID"
  AND o."Category" = 'EXAM_LOCATION'
  AND o."Code" = r."ExamLocationCode";

-- Track invalid option codes on HEX_ExamRecord
INSERT INTO "HEX_Registration3NfConflictReport" ("RecordID", "DivisionID", "FieldCategory", "InvalidCode")
SELECT r."RecordID", r."DivisionID", 'PATIENT_TYPE', r."PatientTypeCode"
FROM "HEX_ExamRecord" r
WHERE r."PatientTypeCode" IS NOT NULL AND r."PatientTypeCode" <> ''
  AND r."PatientTypeOptionID" IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_Registration3NfConflictReport" cr
      WHERE cr."RecordID" = r."RecordID" AND cr."FieldCategory" = 'PATIENT_TYPE'
  );

INSERT INTO "HEX_Registration3NfConflictReport" ("RecordID", "DivisionID", "FieldCategory", "InvalidCode")
SELECT r."RecordID", r."DivisionID", 'PAYMENT_SOURCE', r."PaymentSourceCode"
FROM "HEX_ExamRecord" r
WHERE r."PaymentSourceCode" IS NOT NULL AND r."PaymentSourceCode" <> ''
  AND r."PaymentSourceOptionID" IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_Registration3NfConflictReport" cr
      WHERE cr."RecordID" = r."RecordID" AND cr."FieldCategory" = 'PAYMENT_SOURCE'
  );

INSERT INTO "HEX_Registration3NfConflictReport" ("RecordID", "DivisionID", "FieldCategory", "InvalidCode")
SELECT r."RecordID", r."DivisionID", 'EXAM_LOCATION', r."ExamLocationCode"
FROM "HEX_ExamRecord" r
WHERE r."ExamLocationCode" IS NOT NULL AND r."ExamLocationCode" <> ''
  AND r."ExamLocationOptionID" IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_Registration3NfConflictReport" cr
      WHERE cr."RecordID" = r."RecordID" AND cr."FieldCategory" = 'EXAM_LOCATION'
  );

-- ============================================================================
-- Phase 2: Backfill HEX_Patient
-- ============================================================================

-- Phase 2a: Patients with positive HisPatientID (Shared per DivisionID + PatientID)
WITH ranked_records AS (
    SELECT
        r."DivisionID",
        r."PatientID" AS "HisPatientID",
        COALESCE(r."PatientCode", '') AS "PatientCode",
        COALESCE(r."FullName", '') AS "FullName",
        r."Dob",
        r."BirthYear",
        r."GenderID",
        COALESCE(r."IdentityNumber", '') AS "IdentityNumber",
        r."IdentityIssuedDate",
        (SELECT o."OptionID" FROM "HEX_MasterDataOption" o WHERE o."DivisionID" = r."DivisionID" AND o."Category" = 'IDENTITY_ISSUER' AND o."Code" = r."IdentityIssuerCode" LIMIT 1) AS "IdentityIssuerOptionID",
        COALESCE(r."PhoneNumber", '') AS "PhoneNumber",
        COALESCE(r."Email", '') AS "Email",
        COALESCE(r."Address", '') AS "Address",
        (SELECT o."OptionID" FROM "HEX_MasterDataOption" o WHERE o."DivisionID" = r."DivisionID" AND o."Category" = 'ETHNICITY' AND o."Code" = r."EthnicityCode" LIMIT 1) AS "EthnicityOptionID",
        COALESCE(r."BloodAboCode", '') AS "BloodAboCode",
        COALESCE(r."BloodRhCode", '') AS "BloodRhCode",
        r."CreatedDate",
        r."CreatedBy",
        r."CreatedActorKind",
        r."ModifiedDate",
        r."ModifiedBy",
        r."ModifiedActorKind",
        ROW_NUMBER() OVER (
            PARTITION BY r."DivisionID", r."PatientID"
            ORDER BY r."CreatedDate" DESC, r."ModifiedDate" DESC, r."RecordID" DESC
        ) AS rn
    FROM "HEX_ExamRecord" r
    WHERE r."PatientID" > 0
)
INSERT INTO "HEX_Patient" (
    "PatientRefID",
    "DivisionID",
    "HisPatientID",
    "PatientCode",
    "FullName",
    "Dob",
    "BirthYear",
    "GenderID",
    "IdentityNumber",
    "IdentityIssuedDate",
    "IdentityIssuerOptionID",
    "PhoneNumber",
    "Email",
    "Address",
    "EthnicityOptionID",
    "BloodAboCode",
    "BloodRhCode",
    "HisSyncStatus",
    "HisSyncError",
    "CreatedDate",
    "CreatedBy",
    "CreatedActorKind",
    "ModifiedDate",
    "ModifiedBy",
    "ModifiedActorKind"
)
SELECT
    gen_random_uuid(),
    rr."DivisionID",
    rr."HisPatientID",
    rr."PatientCode",
    rr."FullName",
    rr."Dob",
    rr."BirthYear",
    rr."GenderID",
    rr."IdentityNumber",
    rr."IdentityIssuedDate",
    rr."IdentityIssuerOptionID",
    rr."PhoneNumber",
    rr."Email",
    rr."Address",
    rr."EthnicityOptionID",
    rr."BloodAboCode",
    rr."BloodRhCode",
    'Linked',
    '',
    rr."CreatedDate",
    rr."CreatedBy",
    rr."CreatedActorKind",
    rr."ModifiedDate",
    rr."ModifiedBy",
    rr."ModifiedActorKind"
FROM ranked_records rr
WHERE rr.rn = 1
ON CONFLICT ("DivisionID", "HisPatientID") WHERE "HisPatientID" IS NOT NULL AND "HisPatientID" > 0
DO NOTHING;

-- Link positive HisPatientID records to their patient
UPDATE "HEX_ExamRecord" r
SET "PatientRefID" = p."PatientRefID"
FROM "HEX_Patient" p
WHERE r."DivisionID" = p."DivisionID"
  AND r."PatientID" = p."HisPatientID"
  AND r."PatientID" > 0
  AND r."PatientRefID" IS NULL;

-- Phase 2b: Local Patients for records with PatientID <= 0 or NULL
WITH new_local_patients AS (
    SELECT
        r."RecordID",
        gen_random_uuid() AS new_patient_ref_id
    FROM "HEX_ExamRecord" r
    WHERE (r."PatientID" IS NULL OR r."PatientID" <= 0)
      AND r."PatientRefID" IS NULL
),
inserted_local AS (
    INSERT INTO "HEX_Patient" (
        "PatientRefID",
        "DivisionID",
        "HisPatientID",
        "PatientCode",
        "FullName",
        "Dob",
        "BirthYear",
        "GenderID",
        "IdentityNumber",
        "IdentityIssuedDate",
        "IdentityIssuerOptionID",
        "PhoneNumber",
        "Email",
        "Address",
        "EthnicityOptionID",
        "BloodAboCode",
        "BloodRhCode",
        "HisSyncStatus",
        "HisSyncError",
        "CreatedDate",
        "CreatedBy",
        "CreatedActorKind",
        "ModifiedDate",
        "ModifiedBy",
        "ModifiedActorKind"
    )
    SELECT
        n.new_patient_ref_id,
        r."DivisionID",
        NULL,
        COALESCE(r."PatientCode", ''),
        COALESCE(r."FullName", ''),
        r."Dob",
        r."BirthYear",
        r."GenderID",
        COALESCE(r."IdentityNumber", ''),
        r."IdentityIssuedDate",
        (SELECT o."OptionID" FROM "HEX_MasterDataOption" o WHERE o."DivisionID" = r."DivisionID" AND o."Category" = 'IDENTITY_ISSUER' AND o."Code" = r."IdentityIssuerCode" LIMIT 1),
        COALESCE(r."PhoneNumber", ''),
        COALESCE(r."Email", ''),
        COALESCE(r."Address", ''),
        (SELECT o."OptionID" FROM "HEX_MasterDataOption" o WHERE o."DivisionID" = r."DivisionID" AND o."Category" = 'ETHNICITY' AND o."Code" = r."EthnicityCode" LIMIT 1),
        COALESCE(r."BloodAboCode", ''),
        COALESCE(r."BloodRhCode", ''),
        'Pending',
        '',
        r."CreatedDate",
        r."CreatedBy",
        r."CreatedActorKind",
        r."ModifiedDate",
        r."ModifiedBy",
        r."ModifiedActorKind"
    FROM new_local_patients n
    JOIN "HEX_ExamRecord" r ON r."RecordID" = n."RecordID"
)
UPDATE "HEX_ExamRecord" r
SET "PatientRefID" = n.new_patient_ref_id
FROM new_local_patients n
WHERE r."RecordID" = n."RecordID";

-- Track invalid identity issuer and ethnicity codes on HEX_ExamRecord
INSERT INTO "HEX_Registration3NfConflictReport" ("RecordID", "DivisionID", "FieldCategory", "InvalidCode")
SELECT r."RecordID", r."DivisionID", 'IDENTITY_ISSUER', r."IdentityIssuerCode"
FROM "HEX_ExamRecord" r
WHERE r."IdentityIssuerCode" IS NOT NULL AND r."IdentityIssuerCode" <> ''
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_MasterDataOption" o
      WHERE o."DivisionID" = r."DivisionID"
        AND o."Category" = 'IDENTITY_ISSUER'
        AND o."Code" = r."IdentityIssuerCode"
  )
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_Registration3NfConflictReport" cr
      WHERE cr."RecordID" = r."RecordID" AND cr."FieldCategory" = 'IDENTITY_ISSUER'
  );

INSERT INTO "HEX_Registration3NfConflictReport" ("RecordID", "DivisionID", "FieldCategory", "InvalidCode")
SELECT r."RecordID", r."DivisionID", 'ETHNICITY', r."EthnicityCode"
FROM "HEX_ExamRecord" r
WHERE r."EthnicityCode" IS NOT NULL AND r."EthnicityCode" <> ''
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_MasterDataOption" o
      WHERE o."DivisionID" = r."DivisionID"
        AND o."Category" = 'ETHNICITY'
        AND o."Code" = r."EthnicityCode"
  )
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_Registration3NfConflictReport" cr
      WHERE cr."RecordID" = r."RecordID" AND cr."FieldCategory" = 'ETHNICITY'
  );

-- ============================================================================
-- Phase 3: Backfill Child Facts (Insurance, Employment, Relative)
-- ============================================================================

-- Phase 3a: Insurance
WITH insurance_candidates AS (
    SELECT DISTINCT
        r."DivisionID",
        r."PatientRefID",
        COALESCE(r."InsuranceNumber", '') AS "InsuranceNumber",
        (SELECT o."OptionID" FROM "HEX_MasterDataOption" o WHERE o."DivisionID" = r."DivisionID" AND o."Category" = 'INSURANCE_OBJECT' AND o."Code" = r."InsuranceObjectCode" LIMIT 1) AS "InsuranceObjectOptionID",
        r."InsuranceValidFrom" AS "ValidFrom",
        r."InsuranceValidTo" AS "ValidTo"
    FROM "HEX_ExamRecord" r
    WHERE r."PatientRefID" IS NOT NULL
      AND (
          (r."InsuranceNumber" IS NOT NULL AND r."InsuranceNumber" <> '')
          OR (r."InsuranceObjectCode" IS NOT NULL AND r."InsuranceObjectCode" <> '')
          OR r."InsuranceValidFrom" IS NOT NULL
          OR r."InsuranceValidTo" IS NOT NULL
      )
)
INSERT INTO "HEX_PatientInsurance" (
    "InsuranceRefID",
    "DivisionID",
    "PatientRefID",
    "InsuranceNumber",
    "InsuranceObjectOptionID",
    "ValidFrom",
    "ValidTo",
    "IsActive",
    "CreatedDate",
    "CreatedBy",
    "CreatedActorKind",
    "ModifiedDate",
    "ModifiedBy",
    "ModifiedActorKind"
)
SELECT
    gen_random_uuid(),
    c."DivisionID",
    c."PatientRefID",
    c."InsuranceNumber",
    c."InsuranceObjectOptionID",
    c."ValidFrom",
    c."ValidTo",
    TRUE,
    NOW(),
    1,
    1,
    NOW(),
    1,
    1
FROM insurance_candidates c
WHERE NOT EXISTS (
    SELECT 1 FROM "HEX_PatientInsurance" pi
    WHERE pi."PatientRefID" = c."PatientRefID"
      AND pi."DivisionID" = c."DivisionID"
      AND COALESCE(pi."InsuranceNumber", '') = c."InsuranceNumber"
      AND (pi."InsuranceObjectOptionID" = c."InsuranceObjectOptionID" OR (pi."InsuranceObjectOptionID" IS NULL AND c."InsuranceObjectOptionID" IS NULL))
      AND (pi."ValidFrom" = c."ValidFrom" OR (pi."ValidFrom" IS NULL AND c."ValidFrom" IS NULL))
      AND (pi."ValidTo" = c."ValidTo" OR (pi."ValidTo" IS NULL AND c."ValidTo" IS NULL))
);

UPDATE "HEX_ExamRecord" r
SET "InsuranceRefID" = pi."InsuranceRefID"
FROM "HEX_PatientInsurance" pi
LEFT JOIN "HEX_MasterDataOption" o ON o."OptionID" = pi."InsuranceObjectOptionID"
WHERE r."InsuranceRefID" IS NULL
  AND r."PatientRefID" = pi."PatientRefID"
  AND r."DivisionID" = pi."DivisionID"
  AND COALESCE(r."InsuranceNumber", '') = COALESCE(pi."InsuranceNumber", '')
  AND (r."InsuranceValidFrom" = pi."ValidFrom" OR (r."InsuranceValidFrom" IS NULL AND pi."ValidFrom" IS NULL))
  AND (r."InsuranceValidTo" = pi."ValidTo" OR (r."InsuranceValidTo" IS NULL AND pi."ValidTo" IS NULL))
  AND (
      (r."InsuranceObjectCode" = o."Code")
      OR ((r."InsuranceObjectCode" IS NULL OR r."InsuranceObjectCode" = '') AND pi."InsuranceObjectOptionID" IS NULL)
  );

INSERT INTO "HEX_Registration3NfConflictReport" ("RecordID", "DivisionID", "FieldCategory", "InvalidCode")
SELECT r."RecordID", r."DivisionID", 'INSURANCE_OBJECT', r."InsuranceObjectCode"
FROM "HEX_ExamRecord" r
WHERE r."InsuranceObjectCode" IS NOT NULL AND r."InsuranceObjectCode" <> ''
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_MasterDataOption" o
      WHERE o."DivisionID" = r."DivisionID"
        AND o."Category" = 'INSURANCE_OBJECT'
        AND o."Code" = r."InsuranceObjectCode"
  )
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_Registration3NfConflictReport" cr
      WHERE cr."RecordID" = r."RecordID" AND cr."FieldCategory" = 'INSURANCE_OBJECT'
  );

-- Phase 3b: Employment
WITH employment_candidates AS (
    SELECT DISTINCT
        r."DivisionID",
        r."PatientRefID",
        (SELECT o."OptionID" FROM "HEX_MasterDataOption" o WHERE o."DivisionID" = r."DivisionID" AND o."Category" = 'OCCUPATION' AND o."Code" = r."OccupationCode" LIMIT 1) AS "OccupationOptionID",
        COALESCE(r."StaffCode", '') AS "StaffCode",
        COALESCE(r."OrgDeptName", '') AS "OrgDeptName",
        COALESCE(r."JobTitle", '') AS "JobTitle"
    FROM "HEX_ExamRecord" r
    WHERE r."PatientRefID" IS NOT NULL
      AND (
          (r."OccupationCode" IS NOT NULL AND r."OccupationCode" <> '')
          OR (r."StaffCode" IS NOT NULL AND r."StaffCode" <> '')
          OR (r."OrgDeptName" IS NOT NULL AND r."OrgDeptName" <> '')
          OR (r."JobTitle" IS NOT NULL AND r."JobTitle" <> '')
      )
)
INSERT INTO "HEX_PatientEmployment" (
    "EmploymentRefID",
    "DivisionID",
    "PatientRefID",
    "OccupationOptionID",
    "StaffCode",
    "OrgDeptName",
    "JobTitle",
    "IsActive",
    "CreatedDate",
    "CreatedBy",
    "CreatedActorKind",
    "ModifiedDate",
    "ModifiedBy",
    "ModifiedActorKind"
)
SELECT
    gen_random_uuid(),
    c."DivisionID",
    c."PatientRefID",
    c."OccupationOptionID",
    c."StaffCode",
    c."OrgDeptName",
    c."JobTitle",
    TRUE,
    NOW(),
    1,
    1,
    NOW(),
    1,
    1
FROM employment_candidates c
WHERE NOT EXISTS (
    SELECT 1 FROM "HEX_PatientEmployment" pe
    WHERE pe."PatientRefID" = c."PatientRefID"
      AND pe."DivisionID" = c."DivisionID"
      AND (pe."OccupationOptionID" = c."OccupationOptionID" OR (pe."OccupationOptionID" IS NULL AND c."OccupationOptionID" IS NULL))
      AND COALESCE(pe."StaffCode", '') = c."StaffCode"
      AND COALESCE(pe."OrgDeptName", '') = c."OrgDeptName"
      AND COALESCE(pe."JobTitle", '') = c."JobTitle"
  );

UPDATE "HEX_ExamRecord" r
SET "EmploymentRefID" = pe."EmploymentRefID"
FROM "HEX_PatientEmployment" pe
LEFT JOIN "HEX_MasterDataOption" o ON o."OptionID" = pe."OccupationOptionID"
WHERE r."EmploymentRefID" IS NULL
  AND r."PatientRefID" = pe."PatientRefID"
  AND r."DivisionID" = pe."DivisionID"
  AND COALESCE(r."StaffCode", '') = COALESCE(pe."StaffCode", '')
  AND COALESCE(r."OrgDeptName", '') = COALESCE(pe."OrgDeptName", '')
  AND COALESCE(r."JobTitle", '') = COALESCE(pe."JobTitle", '')
  AND (
      (r."OccupationCode" = o."Code")
      OR ((r."OccupationCode" IS NULL OR r."OccupationCode" = '') AND pe."OccupationOptionID" IS NULL)
  );

INSERT INTO "HEX_Registration3NfConflictReport" ("RecordID", "DivisionID", "FieldCategory", "InvalidCode")
SELECT r."RecordID", r."DivisionID", 'OCCUPATION', r."OccupationCode"
FROM "HEX_ExamRecord" r
WHERE r."OccupationCode" IS NOT NULL AND r."OccupationCode" <> ''
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_MasterDataOption" o
      WHERE o."DivisionID" = r."DivisionID"
        AND o."Category" = 'OCCUPATION'
        AND o."Code" = r."OccupationCode"
  )
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_Registration3NfConflictReport" cr
      WHERE cr."RecordID" = r."RecordID" AND cr."FieldCategory" = 'OCCUPATION'
  );

-- Phase 3c: Relative
WITH relative_candidates AS (
    SELECT DISTINCT
        r."DivisionID",
        r."PatientRefID",
        COALESCE(r."RelativeRelationshipCode", '') AS "RelationshipCode",
        (SELECT o."OptionID" FROM "HEX_MasterDataOption" o WHERE o."DivisionID" = r."DivisionID" AND o."Category" = 'RELATIONSHIP' AND o."Code" = r."RelativeRelationshipCode" LIMIT 1) AS "RelationshipOptionID",
        COALESCE(r."RelativeFullName", '') AS "FullName",
        COALESCE(r."RelativeIdentityNumber", '') AS "IdentityNumber",
        COALESCE(r."RelativePhoneNumber", '') AS "PhoneNumber"
    FROM "HEX_ExamRecord" r
    WHERE r."PatientRefID" IS NOT NULL
      AND (
          (r."RelativeRelationshipCode" IS NOT NULL AND r."RelativeRelationshipCode" <> '')
          OR (r."RelativeFullName" IS NOT NULL AND r."RelativeFullName" <> '')
          OR (r."RelativeIdentityNumber" IS NOT NULL AND r."RelativeIdentityNumber" <> '')
          OR (r."RelativePhoneNumber" IS NOT NULL AND r."RelativePhoneNumber" <> '')
      )
)
INSERT INTO "HEX_PatientRelative" (
    "RelativeRefID",
    "DivisionID",
    "PatientRefID",
    "RelationshipCode",
    "RelationshipOptionID",
    "FullName",
    "IdentityNumber",
    "PhoneNumber",
    "IsActive",
    "CreatedDate",
    "CreatedBy",
    "CreatedActorKind",
    "ModifiedDate",
    "ModifiedBy",
    "ModifiedActorKind"
)
SELECT
    gen_random_uuid(),
    c."DivisionID",
    c."PatientRefID",
    c."RelationshipCode",
    c."RelationshipOptionID",
    c."FullName",
    c."IdentityNumber",
    c."PhoneNumber",
    TRUE,
    NOW(),
    1,
    1,
    NOW(),
    1,
    1
FROM relative_candidates c
WHERE NOT EXISTS (
    SELECT 1 FROM "HEX_PatientRelative" pr
    WHERE pr."PatientRefID" = c."PatientRefID"
      AND pr."DivisionID" = c."DivisionID"
      AND COALESCE(pr."RelationshipCode", '') = c."RelationshipCode"
      AND (pr."RelationshipOptionID" = c."RelationshipOptionID" OR (pr."RelationshipOptionID" IS NULL AND c."RelationshipOptionID" IS NULL))
      AND COALESCE(pr."FullName", '') = c."FullName"
      AND COALESCE(pr."IdentityNumber", '') = c."IdentityNumber"
      AND COALESCE(pr."PhoneNumber", '') = c."PhoneNumber"
);

UPDATE "HEX_ExamRecord" r
SET "RelativeRefID" = pr."RelativeRefID"
FROM "HEX_PatientRelative" pr
WHERE r."RelativeRefID" IS NULL
  AND r."PatientRefID" = pr."PatientRefID"
  AND r."DivisionID" = pr."DivisionID"
  AND COALESCE(r."RelativeRelationshipCode", '') = COALESCE(pr."RelationshipCode", '')
  AND COALESCE(r."RelativeFullName", '') = COALESCE(pr."FullName", '')
  AND COALESCE(r."RelativeIdentityNumber", '') = COALESCE(pr."IdentityNumber", '')
  AND COALESCE(r."RelativePhoneNumber", '') = COALESCE(pr."PhoneNumber", '');

INSERT INTO "HEX_Registration3NfConflictReport" ("RecordID", "DivisionID", "FieldCategory", "InvalidCode")
SELECT r."RecordID", r."DivisionID", 'RELATIONSHIP', r."RelativeRelationshipCode"
FROM "HEX_ExamRecord" r
WHERE r."RelativeRelationshipCode" IS NOT NULL AND r."RelativeRelationshipCode" <> ''
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_MasterDataOption" o
      WHERE o."DivisionID" = r."DivisionID"
        AND o."Category" = 'RELATIONSHIP'
        AND o."Code" = r."RelativeRelationshipCode"
  )
  AND NOT EXISTS (
      SELECT 1 FROM "HEX_Registration3NfConflictReport" cr
      WHERE cr."RecordID" = r."RecordID" AND cr."FieldCategory" = 'RELATIONSHIP'
  );

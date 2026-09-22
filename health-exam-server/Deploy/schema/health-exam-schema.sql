CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE TABLE "HEX_ExamPackage" (
        "PackageID" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "DivisionID" character varying(20) DEFAULT '',
        "PackageCode" character varying(50) NOT NULL,
        "PackageName" character varying(500) NOT NULL,
        "VariantCode" character varying(50),
        "Description" text,
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "CreatedDate" timestamp with time zone NOT NULL,
        "CreatedBy" bigint NOT NULL,
        "CreatedActorKind" smallint NOT NULL,
        "ModifiedDate" timestamp with time zone NOT NULL,
        "ModifiedBy" bigint NOT NULL,
        "ModifiedActorKind" smallint NOT NULL,
        CONSTRAINT "PK_HEX_ExamPackage" PRIMARY KEY ("PackageID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE TABLE "HEX_Organization" (
        "OrganizationID" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "DivisionID" character varying(20) DEFAULT '',
        "OrgCode" character varying(50) NOT NULL,
        "OrgName" character varying(500) NOT NULL,
        "ShortName" character varying(255) DEFAULT '',
        "TaxCode" character varying(20) DEFAULT '',
        "Address" character varying(500) DEFAULT '',
        "ContactName" character varying(255) DEFAULT '',
        "ContactPhone" character varying(20) DEFAULT '',
        "ContactEmail" character varying(100) DEFAULT '',
        "Note" text,
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "CreatedDate" timestamp with time zone NOT NULL,
        "CreatedBy" bigint NOT NULL,
        "CreatedActorKind" smallint NOT NULL,
        "ModifiedDate" timestamp with time zone NOT NULL,
        "ModifiedBy" bigint NOT NULL,
        "ModifiedActorKind" smallint NOT NULL,
        CONSTRAINT "PK_HEX_Organization" PRIMARY KEY ("OrganizationID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE TABLE "HEX_ExamPackageService" (
        "PackageServiceID" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "PackageID" uuid NOT NULL,
        "ServiceID" bigint NOT NULL,
        "ServiceCode" character varying(50) DEFAULT '',
        "ServiceName" character varying(500) DEFAULT '',
        "ParaclinicalKind" character varying(10) DEFAULT '',
        "ServiceGroupCode" character varying(50) DEFAULT '',
        "Quantity" smallint NOT NULL DEFAULT 1,
        "OrderNo" integer NOT NULL,
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "CreatedDate" timestamp with time zone NOT NULL,
        "CreatedBy" bigint NOT NULL,
        "CreatedActorKind" smallint NOT NULL,
        "ModifiedDate" timestamp with time zone NOT NULL,
        "ModifiedBy" bigint NOT NULL,
        "ModifiedActorKind" smallint NOT NULL,
        CONSTRAINT "PK_HEX_ExamPackageService" PRIMARY KEY ("PackageServiceID"),
        CONSTRAINT "FK_HEX_ExamPackageService_HEX_ExamPackage_PackageID" FOREIGN KEY ("PackageID") REFERENCES "HEX_ExamPackage" ("PackageID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE TABLE "HEX_ExamSession" (
        "SessionID" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "DivisionID" character varying(20) DEFAULT '',
        "SessionCode" character varying(50) NOT NULL,
        "SessionName" character varying(500) DEFAULT '',
        "OrganizationID" uuid,
        "OrganizationName" character varying(500) DEFAULT '',
        "ContractNo" character varying(100) DEFAULT '',
        "ContractDate" date,
        "ExamDate" date NOT NULL,
        "ExamDateTo" date,
        "ExamPlace" character varying(500) DEFAULT '',
        "DepartmentID" integer NOT NULL,
        "PackageID" uuid,
        "PackageName" character varying(500) DEFAULT '',
        "VariantCode" character varying(50),
        "State" smallint NOT NULL,
        "ExpectedCount" integer NOT NULL,
        "Note" text,
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "CreatedDate" timestamp with time zone NOT NULL,
        "CreatedBy" bigint NOT NULL,
        "CreatedActorKind" smallint NOT NULL,
        "ModifiedDate" timestamp with time zone NOT NULL,
        "ModifiedBy" bigint NOT NULL,
        "ModifiedActorKind" smallint NOT NULL,
        CONSTRAINT "PK_HEX_ExamSession" PRIMARY KEY ("SessionID"),
        CONSTRAINT "FK_HEX_ExamSession_HEX_ExamPackage_PackageID" FOREIGN KEY ("PackageID") REFERENCES "HEX_ExamPackage" ("PackageID") ON DELETE RESTRICT,
        CONSTRAINT "FK_HEX_ExamSession_HEX_Organization_OrganizationID" FOREIGN KEY ("OrganizationID") REFERENCES "HEX_Organization" ("OrganizationID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE TABLE "HEX_ExamRecord" (
        "RecordID" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "DivisionID" character varying(20) DEFAULT '',
        "SessionID" uuid NOT NULL,
        "RecordCode" character varying(50) NOT NULL,
        "PatientID" bigint NOT NULL,
        "PatientCode" character varying(50) DEFAULT '',
        "FullName" character varying(255) NOT NULL,
        "Dob" date,
        "BirthYear" smallint,
        "GenderID" smallint NOT NULL,
        "IdentityNumber" character varying(20) DEFAULT '',
        "InsuranceNumber" character varying(20) DEFAULT '',
        "PhoneNumber" character varying(20) DEFAULT '',
        "Email" character varying(100) DEFAULT '',
        "Address" character varying(500) DEFAULT '',
        "StaffCode" character varying(50) DEFAULT '',
        "OrgDeptName" character varying(255) DEFAULT '',
        "JobTitle" character varying(255) DEFAULT '',
        "VariantCode" character varying(50) NOT NULL,
        "PackageID" uuid,
        "PackageName" character varying(500) DEFAULT '',
        "FormID" uuid,
        "FormCode" character varying(50) DEFAULT '',
        "SubmissionID" uuid,
        "State" smallint NOT NULL,
        "RegisteredAt" timestamp with time zone,
        "RegisteredBy" bigint NOT NULL,
        "ExamStartedAt" timestamp with time zone,
        "ExamFinishedAt" timestamp with time zone,
        "CancelledAt" timestamp with time zone,
        "CancelledBy" bigint NOT NULL,
        "CancelReason" character varying(500) DEFAULT '',
        "ProgressDone" smallint NOT NULL,
        "ProgressTotal" smallint NOT NULL,
        "ProgressSyncedAt" timestamp with time zone,
        "HealthClassCode" character varying(20) DEFAULT '',
        "ImportBatchID" uuid,
        "Note" text,
        "CreatedDate" timestamp with time zone NOT NULL,
        "CreatedBy" bigint NOT NULL,
        "CreatedActorKind" smallint NOT NULL,
        "ModifiedDate" timestamp with time zone NOT NULL,
        "ModifiedBy" bigint NOT NULL,
        "ModifiedActorKind" smallint NOT NULL,
        CONSTRAINT "PK_HEX_ExamRecord" PRIMARY KEY ("RecordID"),
        CONSTRAINT "FK_HEX_ExamRecord_HEX_ExamPackage_PackageID" FOREIGN KEY ("PackageID") REFERENCES "HEX_ExamPackage" ("PackageID") ON DELETE RESTRICT,
        CONSTRAINT "FK_HEX_ExamRecord_HEX_ExamSession_SessionID" FOREIGN KEY ("SessionID") REFERENCES "HEX_ExamSession" ("SessionID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE UNIQUE INDEX "IX_HEX_ExamPackage_DivisionID_PackageCode" ON "HEX_ExamPackage" ("DivisionID", "PackageCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE UNIQUE INDEX "IX_HEX_ExamPackageService_PackageID_ServiceID" ON "HEX_ExamPackageService" ("PackageID", "ServiceID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE INDEX "IX_HEX_PkgService_Pkg" ON "HEX_ExamPackageService" ("PackageID", "OrderNo") WHERE "IsActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE UNIQUE INDEX "IX_HEX_ExamRecord_DivisionID_RecordCode" ON "HEX_ExamRecord" ("DivisionID", "RecordCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE INDEX "IX_HEX_ExamRecord_PackageID" ON "HEX_ExamRecord" ("PackageID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE INDEX "IX_HEX_Record_Session" ON "HEX_ExamRecord" ("SessionID", "State");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE UNIQUE INDEX "UX_HEX_Record_Session_Identity" ON "HEX_ExamRecord" ("SessionID", "IdentityNumber") WHERE "IdentityNumber" <> '' AND "State" <> 4;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE UNIQUE INDEX "UX_HEX_Record_Session_Patient" ON "HEX_ExamRecord" ("SessionID", "PatientCode") WHERE "PatientCode" <> '' AND "State" <> 4;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE UNIQUE INDEX "IX_HEX_ExamSession_DivisionID_SessionCode" ON "HEX_ExamSession" ("DivisionID", "SessionCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE INDEX "IX_HEX_ExamSession_OrganizationID" ON "HEX_ExamSession" ("OrganizationID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE INDEX "IX_HEX_ExamSession_PackageID" ON "HEX_ExamSession" ("PackageID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE INDEX "IX_HEX_Session_Date" ON "HEX_ExamSession" ("DivisionID", "ExamDate" DESC) WHERE "IsActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE INDEX "IX_HEX_Session_Org" ON "HEX_ExamSession" ("DivisionID", "OrganizationID", "ExamDate" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE INDEX "IX_HEX_Org_Name" ON "HEX_Organization" ("DivisionID", "OrgName") WHERE "IsActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    CREATE UNIQUE INDEX "IX_HEX_Organization_DivisionID_OrgCode" ON "HEX_Organization" ("DivisionID", "OrgCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824083342_InitialHealthExamSchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260824083342_InitialHealthExamSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824095039_AddAuditLog') THEN
    CREATE TABLE "HEX_AuditLog" (
        "AuditID" bigint GENERATED ALWAYS AS IDENTITY,
        "DivisionID" character varying(20) DEFAULT '',
        "EntityType" character varying(30) NOT NULL,
        "EntityID" uuid NOT NULL,
        "Action" character varying(30) NOT NULL,
        "ActorID" bigint NOT NULL,
        "ActorKind" smallint NOT NULL,
        "ActorName" character varying(255) DEFAULT '',
        "FromState" smallint,
        "ToState" smallint,
        "Payload" jsonb,
        "TraceID" character varying(50) DEFAULT '',
        "OccurredAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_HEX_AuditLog" PRIMARY KEY ("AuditID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824095039_AddAuditLog') THEN
    CREATE INDEX "IX_HEX_Audit_Division" ON "HEX_AuditLog" ("DivisionID", "OccurredAt" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824095039_AddAuditLog') THEN
    CREATE INDEX "IX_HEX_Audit_Entity" ON "HEX_AuditLog" ("EntityType", "EntityID", "OccurredAt" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824095039_AddAuditLog') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260824095039_AddAuditLog', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824100146_AddSessionRecordCounter') THEN
    ALTER TABLE "HEX_ExamSession" ADD "LastRecordNo" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824100146_AddSessionRecordCounter') THEN
    UPDATE "HEX_ExamSession" s
       SET "LastRecordNo" = (SELECT COUNT(*) FROM "HEX_ExamRecord" r
                              WHERE r."SessionID" = s."SessionID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824100146_AddSessionRecordCounter') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260824100146_AddSessionRecordCounter', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824122513_AddImportBatch') THEN
    CREATE TABLE "HEX_ImportBatch" (
        "BatchID" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "DivisionID" character varying(20) DEFAULT '',
        "SessionID" uuid NOT NULL,
        "FileName" character varying(500) NOT NULL,
        "StoragePath" text DEFAULT '',
        "SheetName" character varying(100) DEFAULT '',
        "TotalRow" integer NOT NULL,
        "SuccessRow" integer NOT NULL,
        "ErrorRow" integer NOT NULL,
        "State" smallint NOT NULL,
        "StartedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        "FinishedAt" timestamp with time zone,
        "ErrorSummary" character varying(1000) DEFAULT '',
        "CreatedRecordCount" integer NOT NULL,
        "CreatedDate" timestamp with time zone NOT NULL,
        "CreatedBy" bigint NOT NULL,
        "CreatedActorKind" smallint NOT NULL,
        "ModifiedDate" timestamp with time zone NOT NULL,
        "ModifiedBy" bigint NOT NULL,
        "ModifiedActorKind" smallint NOT NULL,
        CONSTRAINT "PK_HEX_ImportBatch" PRIMARY KEY ("BatchID"),
        CONSTRAINT "FK_HEX_ImportBatch_HEX_ExamSession_SessionID" FOREIGN KEY ("SessionID") REFERENCES "HEX_ExamSession" ("SessionID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824122513_AddImportBatch') THEN
    CREATE TABLE "HEX_ImportBatchRow" (
        "ImportRowID" bigint GENERATED ALWAYS AS IDENTITY,
        "BatchID" uuid NOT NULL,
        "RowNo" integer NOT NULL,
        "RawData" jsonb NOT NULL,
        "IsValid" boolean NOT NULL,
        "ErrorCode" character varying(50) DEFAULT '',
        "ErrorMessage" character varying(1000) DEFAULT '',
        "RecordID" uuid,
        CONSTRAINT "PK_HEX_ImportBatchRow" PRIMARY KEY ("ImportRowID"),
        CONSTRAINT "FK_HEX_ImportBatchRow_HEX_ImportBatch_BatchID" FOREIGN KEY ("BatchID") REFERENCES "HEX_ImportBatch" ("BatchID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824122513_AddImportBatch') THEN
    CREATE INDEX "IX_HEX_Import_Session" ON "HEX_ImportBatch" ("SessionID", "StartedAt" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824122513_AddImportBatch') THEN
    CREATE INDEX "IX_HEX_ImportRow_Error" ON "HEX_ImportBatchRow" ("BatchID", "RowNo") WHERE NOT "IsValid";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824122513_AddImportBatch') THEN
    CREATE UNIQUE INDEX "UX_HEX_ImportRow_Batch_RowNo" ON "HEX_ImportBatchRow" ("BatchID", "RowNo");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824122513_AddImportBatch') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260824122513_AddImportBatch', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824131248_AddWebhookInbox') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "LastEventAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824131248_AddWebhookInbox') THEN
    CREATE TABLE "HEX_WebhookInbox" (
        "InboxID" bigint GENERATED ALWAYS AS IDENTITY,
        "EventID" character varying(100) NOT NULL,
        "EventType" character varying(50) NOT NULL,
        "DivisionID" character varying(20) DEFAULT '',
        "SubmissionID" uuid,
        "RecordID" uuid,
        "Payload" jsonb NOT NULL,
        "OccurredAt" timestamp with time zone NOT NULL,
        "ReceivedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        "ProcessedAt" timestamp with time zone,
        "ProcessState" smallint NOT NULL,
        "RetryCount" smallint NOT NULL,
        "LastError" character varying(1000) DEFAULT '',
        "TraceID" character varying(50) DEFAULT '',
        CONSTRAINT "PK_HEX_WebhookInbox" PRIMARY KEY ("InboxID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824131248_AddWebhookInbox') THEN
    CREATE INDEX "IX_HEX_Inbox_Pending" ON "HEX_WebhookInbox" ("ProcessState", "ReceivedAt") WHERE "ProcessState" IN (0, 2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824131248_AddWebhookInbox') THEN
    CREATE UNIQUE INDEX "UX_HEX_Inbox_Event" ON "HEX_WebhookInbox" ("EventID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824131248_AddWebhookInbox') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260824131248_AddWebhookInbox', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824171805_AddWebhookRetrySchedule') THEN
    DROP INDEX "IX_HEX_Inbox_Pending";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824171805_AddWebhookRetrySchedule') THEN
    DROP INDEX "UX_HEX_Inbox_Event";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824171805_AddWebhookRetrySchedule') THEN
    ALTER TABLE "HEX_WebhookInbox" ADD "NextAttemptAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824171805_AddWebhookRetrySchedule') THEN
    CREATE INDEX "IX_HEX_Inbox_Pending" ON "HEX_WebhookInbox" ("ProcessState", "NextAttemptAt", "ReceivedAt") WHERE "ProcessState" IN (0, 2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824171805_AddWebhookRetrySchedule') THEN
    CREATE UNIQUE INDEX "UX_HEX_Inbox_Event" ON "HEX_WebhookInbox" ("DivisionID", "EventID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824171805_AddWebhookRetrySchedule') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260824171805_AddWebhookRetrySchedule', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824201107_AddExamTimestampEstimatedFlags') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "ExamFinishedAtEstimated" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824201107_AddExamTimestampEstimatedFlags') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "ExamStartedAtEstimated" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824201107_AddExamTimestampEstimatedFlags') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260824201107_AddExamTimestampEstimatedFlags', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    CREATE SEQUENCE "HEX_ParaclinicalOrderNo" START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE NO CYCLE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    ALTER TABLE "HEX_WebhookInbox" ADD "MessageID" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    CREATE TABLE "HEX_ParaclinicalOrder" (
        "OrderID" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "DivisionID" character varying(20) DEFAULT '',
        "RecordID" uuid NOT NULL,
        "SessionID" uuid NOT NULL,
        "OrderNo" character varying(50) NOT NULL,
        "ParaclinicalKind" character varying(10) DEFAULT '',
        "SourcePackageID" uuid,
        "OrderedByID" bigint NOT NULL,
        "OrderedByName" character varying(255) DEFAULT '',
        "OrderedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        "RoomID" integer NOT NULL,
        "TargetSystem" character varying(20) DEFAULT 'NONE',
        "SentStatus" smallint NOT NULL,
        "SentAt" timestamp with time zone,
        "ExternalOrderID" character varying(100) DEFAULT '',
        "Note" character varying(1000) DEFAULT '',
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "CreatedDate" timestamp with time zone NOT NULL,
        "CreatedBy" bigint NOT NULL,
        "CreatedActorKind" smallint NOT NULL,
        "ModifiedDate" timestamp with time zone NOT NULL,
        "ModifiedBy" bigint NOT NULL,
        "ModifiedActorKind" smallint NOT NULL,
        CONSTRAINT "PK_HEX_ParaclinicalOrder" PRIMARY KEY ("OrderID"),
        CONSTRAINT "FK_HEX_ParaclinicalOrder_HEX_ExamRecord_RecordID" FOREIGN KEY ("RecordID") REFERENCES "HEX_ExamRecord" ("RecordID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    CREATE TABLE "HEX_ParaclinicalOrderItem" (
        "OrderItemID" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "OrderID" uuid NOT NULL,
        "RecordID" uuid NOT NULL,
        "DivisionID" character varying(20) DEFAULT '',
        "ServiceID" bigint NOT NULL,
        "ServiceCode" character varying(50) DEFAULT '',
        "ServiceName" character varying(500) DEFAULT '',
        "ServiceGroupCode" character varying(50) DEFAULT '',
        "Quantity" smallint NOT NULL DEFAULT 1,
        "State" smallint NOT NULL,
        "PerformedAt" timestamp with time zone,
        "ResultAt" timestamp with time zone,
        "ResultSourceKind" character varying(10) DEFAULT '',
        "ResultRefID" character varying(100),
        "AttachmentID" uuid,
        "IsAbnormal" boolean,
        "MessageID" character varying(100),
        "CancelledAt" timestamp with time zone,
        "CancelReason" character varying(500) DEFAULT '',
        "SourcePackageID" uuid,
        "CreatedDate" timestamp with time zone NOT NULL,
        "CreatedBy" bigint NOT NULL,
        "CreatedActorKind" smallint NOT NULL,
        "ModifiedDate" timestamp with time zone NOT NULL,
        "ModifiedBy" bigint NOT NULL,
        "ModifiedActorKind" smallint NOT NULL,
        CONSTRAINT "PK_HEX_ParaclinicalOrderItem" PRIMARY KEY ("OrderItemID"),
        CONSTRAINT "FK_HEX_ParaclinicalOrderItem_HEX_ParaclinicalOrder_OrderID" FOREIGN KEY ("OrderID") REFERENCES "HEX_ParaclinicalOrder" ("OrderID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    CREATE UNIQUE INDEX "UX_HEX_Inbox_MessageID" ON "HEX_WebhookInbox" ("DivisionID", "MessageID") WHERE "MessageID" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    CREATE INDEX "IX_HEX_Order_Record" ON "HEX_ParaclinicalOrder" ("RecordID", "OrderedAt" DESC) WHERE "IsActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    CREATE INDEX "IX_HEX_Order_Session" ON "HEX_ParaclinicalOrder" ("SessionID", "OrderedAt" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    CREATE UNIQUE INDEX "UX_HEX_Order_No" ON "HEX_ParaclinicalOrder" ("DivisionID", "OrderNo");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    CREATE INDEX "IX_HEX_OrderItem_Record" ON "HEX_ParaclinicalOrderItem" ("RecordID", "State");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    CREATE UNIQUE INDEX "UX_HEX_OrderItem_MessageID" ON "HEX_ParaclinicalOrderItem" ("DivisionID", "MessageID") WHERE "MessageID" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    CREATE UNIQUE INDEX "UX_HEX_OrderItem_Service" ON "HEX_ParaclinicalOrderItem" ("OrderID", "ServiceID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825204654_AddParaclinicalOrder') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260825204654_AddParaclinicalOrder', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826060348_AddIntegrationOutbox') THEN
    CREATE SEQUENCE "HEX_ParaclinicalVendorLineNo" START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE NO CYCLE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826060348_AddIntegrationOutbox') THEN
    ALTER TABLE "HEX_ParaclinicalOrderItem" ADD "VendorLineNo" bigint;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826060348_AddIntegrationOutbox') THEN
    CREATE TABLE "HEX_IntegrationOutbox" (
        "OutboxID" bigint GENERATED ALWAYS AS IDENTITY,
        "DivisionID" character varying(20) DEFAULT '',
        "OrderID" uuid NOT NULL,
        "Vendor" character varying(20) NOT NULL,
        "Operation" character varying(20) NOT NULL,
        "DedupKey" character varying(120) NOT NULL,
        "Payload" jsonb NOT NULL,
        "State" smallint NOT NULL,
        "RetryCount" smallint NOT NULL,
        "NextAttemptAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        "SentAt" timestamp with time zone,
        "LastError" character varying(1000) DEFAULT '',
        "ResponseSnippet" character varying(1000) DEFAULT '',
        "TraceID" character varying(50) DEFAULT '',
        CONSTRAINT "PK_HEX_IntegrationOutbox" PRIMARY KEY ("OutboxID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826060348_AddIntegrationOutbox') THEN
    CREATE UNIQUE INDEX "UX_HEX_OrderItem_VendorLineNo" ON "HEX_ParaclinicalOrderItem" ("DivisionID", "VendorLineNo") WHERE "VendorLineNo" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826060348_AddIntegrationOutbox') THEN
    CREATE INDEX "IX_HEX_Outbox_Order" ON "HEX_IntegrationOutbox" ("OrderID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826060348_AddIntegrationOutbox') THEN
    CREATE INDEX "IX_HEX_Outbox_Pending" ON "HEX_IntegrationOutbox" ("State", "NextAttemptAt", "CreatedAt") WHERE "State" IN (0, 2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826060348_AddIntegrationOutbox') THEN
    CREATE UNIQUE INDEX "UX_HEX_Outbox_Dedup" ON "HEX_IntegrationOutbox" ("DivisionID", "DedupKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826060348_AddIntegrationOutbox') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260826060348_AddIntegrationOutbox', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    DROP INDEX "UX_HEX_Record_Session_Identity";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    DROP INDEX "UX_HEX_Record_Session_Patient";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "BloodAboCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "BloodAboName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "BloodRhCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "BloodRhName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "EthnicityCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "EthnicityName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "ExamLocationCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "ExamLocationName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "ExamReason" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "IdentityIssuedDate" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "IdentityIssuerCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "IdentityIssuerName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "InsuranceObjectCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "InsuranceObjectName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "InsuranceValidFrom" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "InsuranceValidTo" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "OccupationCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "OccupationName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "PatientTypeCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "PatientTypeName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "PaymentSourceCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "PaymentSourceName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "PaymentSourceOther" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "ProvinceCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "ProvinceName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "RelativeFullName" character varying(255) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "RelativeIdentityNumber" character varying(20) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "RelativePhoneNumber" character varying(20) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "RelativeRelationshipCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "RelativeRelationshipName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "WardCode" character varying(50) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    ALTER TABLE "HEX_ExamRecord" ADD "WardName" character varying(500) DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    CREATE TABLE "HEX_MasterDataOption" (
        "OptionID" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "DivisionID" character varying(20) DEFAULT '',
        "Category" character varying(50) NOT NULL,
        "Code" character varying(50) NOT NULL,
        "Name" character varying(500) NOT NULL,
        "ParentCode" character varying(50) DEFAULT '',
        "OrderNo" integer NOT NULL,
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "CreatedDate" timestamp with time zone NOT NULL,
        "CreatedBy" bigint NOT NULL,
        "CreatedActorKind" smallint NOT NULL,
        "ModifiedDate" timestamp with time zone NOT NULL,
        "ModifiedBy" bigint NOT NULL,
        "ModifiedActorKind" smallint NOT NULL,
        CONSTRAINT "PK_HEX_MasterDataOption" PRIMARY KEY ("OptionID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    CREATE UNIQUE INDEX "UX_HEX_Record_Session_Identity" ON "HEX_ExamRecord" ("SessionID", "IdentityNumber") WHERE "IdentityNumber" <> '' AND "State" NOT IN (4, 5);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    CREATE UNIQUE INDEX "UX_HEX_Record_Session_Patient" ON "HEX_ExamRecord" ("SessionID", "PatientCode") WHERE "PatientCode" <> '' AND "State" NOT IN (4, 5);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    CREATE UNIQUE INDEX "IX_HEX_MasterDataOption_DivisionID_Category_Code" ON "HEX_MasterDataOption" ("DivisionID", "Category", "Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    CREATE INDEX "IX_HEX_MasterDataOption_DivisionID_Category_IsActive_OrderNo" ON "HEX_MasterDataOption" ("DivisionID", "Category", "IsActive", "OrderNo");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    CREATE INDEX "IX_HEX_MasterDataOption_DivisionID_Category_ParentCode_IsActive" ON "HEX_MasterDataOption" ("DivisionID", "Category", "ParentCode", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907095757_AddRegistrationMasterData') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260907095757_AddRegistrationMasterData', '8.0.11');
    END IF;
END $EF$;
COMMIT;


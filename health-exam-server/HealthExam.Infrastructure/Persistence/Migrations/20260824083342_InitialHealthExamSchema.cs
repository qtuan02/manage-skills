using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialHealthExamSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.CreateTable(
                name: "HEX_ExamPackage",
                columns: table => new
                {
                    PackageID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    PackageCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PackageName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    VariantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedActorKind = table.Column<short>(type: "smallint", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedActorKind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ExamPackage", x => x.PackageID);
                });

            migrationBuilder.CreateTable(
                name: "HEX_Organization",
                columns: table => new
                {
                    OrganizationID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    OrgCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OrgName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ShortName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
                    TaxCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    ContactName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
                    ContactPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    ContactEmail = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, defaultValue: ""),
                    Note = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedActorKind = table.Column<short>(type: "smallint", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedActorKind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_Organization", x => x.OrganizationID);
                });

            migrationBuilder.CreateTable(
                name: "HEX_ExamPackageService",
                columns: table => new
                {
                    PackageServiceID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    PackageID = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceID = table.Column<long>(type: "bigint", nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    ServiceName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    ParaclinicalKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true, defaultValue: ""),
                    ServiceGroupCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    Quantity = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    OrderNo = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedActorKind = table.Column<short>(type: "smallint", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedActorKind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ExamPackageService", x => x.PackageServiceID);
                    table.ForeignKey(
                        name: "FK_HEX_ExamPackageService_HEX_ExamPackage_PackageID",
                        column: x => x.PackageID,
                        principalTable: "HEX_ExamPackage",
                        principalColumn: "PackageID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HEX_ExamSession",
                columns: table => new
                {
                    SessionID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    SessionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SessionName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    OrganizationID = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    ContractNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, defaultValue: ""),
                    ContractDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExamDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExamDateTo = table.Column<DateOnly>(type: "date", nullable: true),
                    ExamPlace = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    DepartmentID = table.Column<int>(type: "integer", nullable: false),
                    PackageID = table.Column<Guid>(type: "uuid", nullable: true),
                    PackageName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    VariantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedCount = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedActorKind = table.Column<short>(type: "smallint", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedActorKind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ExamSession", x => x.SessionID);
                    table.ForeignKey(
                        name: "FK_HEX_ExamSession_HEX_ExamPackage_PackageID",
                        column: x => x.PackageID,
                        principalTable: "HEX_ExamPackage",
                        principalColumn: "PackageID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HEX_ExamSession_HEX_Organization_OrganizationID",
                        column: x => x.OrganizationID,
                        principalTable: "HEX_Organization",
                        principalColumn: "OrganizationID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HEX_ExamRecord",
                columns: table => new
                {
                    RecordID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    SessionID = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PatientID = table.Column<long>(type: "bigint", nullable: false),
                    PatientCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    FullName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Dob = table.Column<DateOnly>(type: "date", nullable: true),
                    BirthYear = table.Column<short>(type: "smallint", nullable: true),
                    GenderID = table.Column<short>(type: "smallint", nullable: false),
                    IdentityNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    InsuranceNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, defaultValue: ""),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    StaffCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    OrgDeptName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
                    JobTitle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
                    VariantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PackageID = table.Column<Guid>(type: "uuid", nullable: true),
                    PackageName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    FormID = table.Column<Guid>(type: "uuid", nullable: true),
                    FormCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    SubmissionID = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RegisteredBy = table.Column<long>(type: "bigint", nullable: false),
                    ExamStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExamFinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledBy = table.Column<long>(type: "bigint", nullable: false),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    ProgressDone = table.Column<short>(type: "smallint", nullable: false),
                    ProgressTotal = table.Column<short>(type: "smallint", nullable: false),
                    ProgressSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HealthClassCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    ImportBatchID = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedActorKind = table.Column<short>(type: "smallint", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedActorKind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ExamRecord", x => x.RecordID);
                    table.ForeignKey(
                        name: "FK_HEX_ExamRecord_HEX_ExamPackage_PackageID",
                        column: x => x.PackageID,
                        principalTable: "HEX_ExamPackage",
                        principalColumn: "PackageID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HEX_ExamRecord_HEX_ExamSession_SessionID",
                        column: x => x.SessionID,
                        principalTable: "HEX_ExamSession",
                        principalColumn: "SessionID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamPackage_DivisionID_PackageCode",
                table: "HEX_ExamPackage",
                columns: new[] { "DivisionID", "PackageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamPackageService_PackageID_ServiceID",
                table: "HEX_ExamPackageService",
                columns: new[] { "PackageID", "ServiceID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_PkgService_Pkg",
                table: "HEX_ExamPackageService",
                columns: new[] { "PackageID", "OrderNo" },
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamRecord_DivisionID_RecordCode",
                table: "HEX_ExamRecord",
                columns: new[] { "DivisionID", "RecordCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamRecord_PackageID",
                table: "HEX_ExamRecord",
                column: "PackageID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Record_Session",
                table: "HEX_ExamRecord",
                columns: new[] { "SessionID", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Record_Session_Identity",
                table: "HEX_ExamRecord",
                columns: new[] { "SessionID", "IdentityNumber" },
                unique: true,
                filter: "\"IdentityNumber\" <> '' AND \"State\" <> 4");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Record_Session_Patient",
                table: "HEX_ExamRecord",
                columns: new[] { "SessionID", "PatientCode" },
                unique: true,
                filter: "\"PatientCode\" <> '' AND \"State\" <> 4");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamSession_DivisionID_SessionCode",
                table: "HEX_ExamSession",
                columns: new[] { "DivisionID", "SessionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamSession_OrganizationID",
                table: "HEX_ExamSession",
                column: "OrganizationID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamSession_PackageID",
                table: "HEX_ExamSession",
                column: "PackageID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Session_Date",
                table: "HEX_ExamSession",
                columns: new[] { "DivisionID", "ExamDate" },
                descending: new[] { false, true },
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Session_Org",
                table: "HEX_ExamSession",
                columns: new[] { "DivisionID", "OrganizationID", "ExamDate" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Org_Name",
                table: "HEX_Organization",
                columns: new[] { "DivisionID", "OrgName" },
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Organization_DivisionID_OrgCode",
                table: "HEX_Organization",
                columns: new[] { "DivisionID", "OrgCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_ExamPackageService");

            migrationBuilder.DropTable(
                name: "HEX_ExamRecord");

            migrationBuilder.DropTable(
                name: "HEX_ExamSession");

            migrationBuilder.DropTable(
                name: "HEX_ExamPackage");

            migrationBuilder.DropTable(
                name: "HEX_Organization");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeRegistration3Nf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmploymentRefID",
                table: "HEX_ExamRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExamLocationOptionID",
                table: "HEX_ExamRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InsuranceRefID",
                table: "HEX_ExamRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PatientRefID",
                table: "HEX_ExamRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PatientTypeOptionID",
                table: "HEX_ExamRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentSourceOptionID",
                table: "HEX_ExamRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelativeRefID",
                table: "HEX_ExamRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HEX_Patient",
                columns: table => new
                {
                    PatientRefID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    HisPatientID = table.Column<long>(type: "bigint", nullable: true),
                    PatientCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    FullName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Dob = table.Column<DateOnly>(type: "date", nullable: true),
                    BirthYear = table.Column<short>(type: "smallint", nullable: true),
                    GenderID = table.Column<short>(type: "smallint", nullable: false),
                    IdentityNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    IdentityIssuedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IdentityIssuerOptionID = table.Column<Guid>(type: "uuid", nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, defaultValue: ""),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    EthnicityOptionID = table.Column<Guid>(type: "uuid", nullable: true),
                    BloodAboCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    BloodRhCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    HisSyncStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    HisSyncError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedActorKind = table.Column<short>(type: "smallint", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedActorKind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_Patient", x => x.PatientRefID);
                    table.ForeignKey(
                        name: "FK_HEX_Patient_HEX_MasterDataOption_EthnicityOptionID",
                        column: x => x.EthnicityOptionID,
                        principalTable: "HEX_MasterDataOption",
                        principalColumn: "OptionID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HEX_Patient_HEX_MasterDataOption_IdentityIssuerOptionID",
                        column: x => x.IdentityIssuerOptionID,
                        principalTable: "HEX_MasterDataOption",
                        principalColumn: "OptionID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HEX_PatientEmployment",
                columns: table => new
                {
                    EmploymentRefID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    PatientRefID = table.Column<Guid>(type: "uuid", nullable: false),
                    OccupationOptionID = table.Column<Guid>(type: "uuid", nullable: true),
                    StaffCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    OrgDeptName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
                    JobTitle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
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
                    table.PrimaryKey("PK_HEX_PatientEmployment", x => x.EmploymentRefID);
                    table.ForeignKey(
                        name: "FK_HEX_PatientEmployment_HEX_MasterDataOption_OccupationOption~",
                        column: x => x.OccupationOptionID,
                        principalTable: "HEX_MasterDataOption",
                        principalColumn: "OptionID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HEX_PatientEmployment_HEX_Patient_PatientRefID",
                        column: x => x.PatientRefID,
                        principalTable: "HEX_Patient",
                        principalColumn: "PatientRefID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HEX_PatientInsurance",
                columns: table => new
                {
                    InsuranceRefID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    PatientRefID = table.Column<Guid>(type: "uuid", nullable: false),
                    InsuranceNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    InsuranceObjectOptionID = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_HEX_PatientInsurance", x => x.InsuranceRefID);
                    table.ForeignKey(
                        name: "FK_HEX_PatientInsurance_HEX_MasterDataOption_InsuranceObjectOp~",
                        column: x => x.InsuranceObjectOptionID,
                        principalTable: "HEX_MasterDataOption",
                        principalColumn: "OptionID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HEX_PatientInsurance_HEX_Patient_PatientRefID",
                        column: x => x.PatientRefID,
                        principalTable: "HEX_Patient",
                        principalColumn: "PatientRefID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HEX_PatientRelative",
                columns: table => new
                {
                    RelativeRefID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    PatientRefID = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationshipCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    RelationshipOptionID = table.Column<Guid>(type: "uuid", nullable: true),
                    FullName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
                    IdentityNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
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
                    table.PrimaryKey("PK_HEX_PatientRelative", x => x.RelativeRefID);
                    table.ForeignKey(
                        name: "FK_HEX_PatientRelative_HEX_Patient_PatientRefID",
                        column: x => x.PatientRefID,
                        principalTable: "HEX_Patient",
                        principalColumn: "PatientRefID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamRecord_ExamLocationOptionID",
                table: "HEX_ExamRecord",
                column: "ExamLocationOptionID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamRecord_PatientTypeOptionID",
                table: "HEX_ExamRecord",
                column: "PatientTypeOptionID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamRecord_PaymentSourceOptionID",
                table: "HEX_ExamRecord",
                column: "PaymentSourceOptionID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Record_EmploymentRefID",
                table: "HEX_ExamRecord",
                column: "EmploymentRefID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Record_InsuranceRefID",
                table: "HEX_ExamRecord",
                column: "InsuranceRefID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Record_PatientRefID",
                table: "HEX_ExamRecord",
                column: "PatientRefID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Record_RelativeRefID",
                table: "HEX_ExamRecord",
                column: "RelativeRefID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Patient_Division_Code",
                table: "HEX_Patient",
                columns: new[] { "DivisionID", "PatientCode" });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Patient_EthnicityOptionID",
                table: "HEX_Patient",
                column: "EthnicityOptionID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Patient_Identity",
                table: "HEX_Patient",
                columns: new[] { "DivisionID", "IdentityNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Patient_IdentityIssuerOptionID",
                table: "HEX_Patient",
                column: "IdentityIssuerOptionID");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Patient_Division_HisPatientID",
                table: "HEX_Patient",
                columns: new[] { "DivisionID", "HisPatientID" },
                unique: true,
                filter: "\"HisPatientID\" IS NOT NULL AND \"HisPatientID\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_PatientEmployment_OccupationOptionID",
                table: "HEX_PatientEmployment",
                column: "OccupationOptionID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_PatientEmployment_Patient",
                table: "HEX_PatientEmployment",
                column: "PatientRefID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_PatientInsurance_InsuranceObjectOptionID",
                table: "HEX_PatientInsurance",
                column: "InsuranceObjectOptionID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_PatientInsurance_Number",
                table: "HEX_PatientInsurance",
                columns: new[] { "DivisionID", "InsuranceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_PatientInsurance_Patient",
                table: "HEX_PatientInsurance",
                column: "PatientRefID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_PatientRelative_Patient",
                table: "HEX_PatientRelative",
                column: "PatientRefID");

            migrationBuilder.AddForeignKey(
                name: "FK_HEX_ExamRecord_HEX_MasterDataOption_ExamLocationOptionID",
                table: "HEX_ExamRecord",
                column: "ExamLocationOptionID",
                principalTable: "HEX_MasterDataOption",
                principalColumn: "OptionID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HEX_ExamRecord_HEX_MasterDataOption_PatientTypeOptionID",
                table: "HEX_ExamRecord",
                column: "PatientTypeOptionID",
                principalTable: "HEX_MasterDataOption",
                principalColumn: "OptionID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HEX_ExamRecord_HEX_MasterDataOption_PaymentSourceOptionID",
                table: "HEX_ExamRecord",
                column: "PaymentSourceOptionID",
                principalTable: "HEX_MasterDataOption",
                principalColumn: "OptionID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HEX_ExamRecord_HEX_PatientEmployment_EmploymentRefID",
                table: "HEX_ExamRecord",
                column: "EmploymentRefID",
                principalTable: "HEX_PatientEmployment",
                principalColumn: "EmploymentRefID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HEX_ExamRecord_HEX_PatientInsurance_InsuranceRefID",
                table: "HEX_ExamRecord",
                column: "InsuranceRefID",
                principalTable: "HEX_PatientInsurance",
                principalColumn: "InsuranceRefID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HEX_ExamRecord_HEX_PatientRelative_RelativeRefID",
                table: "HEX_ExamRecord",
                column: "RelativeRefID",
                principalTable: "HEX_PatientRelative",
                principalColumn: "RelativeRefID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HEX_ExamRecord_HEX_Patient_PatientRefID",
                table: "HEX_ExamRecord",
                column: "PatientRefID",
                principalTable: "HEX_Patient",
                principalColumn: "PatientRefID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HEX_ExamRecord_HEX_MasterDataOption_ExamLocationOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropForeignKey(
                name: "FK_HEX_ExamRecord_HEX_MasterDataOption_PatientTypeOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropForeignKey(
                name: "FK_HEX_ExamRecord_HEX_MasterDataOption_PaymentSourceOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropForeignKey(
                name: "FK_HEX_ExamRecord_HEX_PatientEmployment_EmploymentRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropForeignKey(
                name: "FK_HEX_ExamRecord_HEX_PatientInsurance_InsuranceRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropForeignKey(
                name: "FK_HEX_ExamRecord_HEX_PatientRelative_RelativeRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropForeignKey(
                name: "FK_HEX_ExamRecord_HEX_Patient_PatientRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropTable(
                name: "HEX_PatientEmployment");

            migrationBuilder.DropTable(
                name: "HEX_PatientInsurance");

            migrationBuilder.DropTable(
                name: "HEX_PatientRelative");

            migrationBuilder.DropTable(
                name: "HEX_Patient");

            migrationBuilder.DropIndex(
                name: "IX_HEX_ExamRecord_ExamLocationOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropIndex(
                name: "IX_HEX_ExamRecord_PatientTypeOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropIndex(
                name: "IX_HEX_ExamRecord_PaymentSourceOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropIndex(
                name: "IX_HEX_Record_EmploymentRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropIndex(
                name: "IX_HEX_Record_InsuranceRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropIndex(
                name: "IX_HEX_Record_PatientRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropIndex(
                name: "IX_HEX_Record_RelativeRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "EmploymentRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "ExamLocationOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "InsuranceRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "PatientRefID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "PatientTypeOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "PaymentSourceOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "RelativeRefID",
                table: "HEX_ExamRecord");
        }
    }
}

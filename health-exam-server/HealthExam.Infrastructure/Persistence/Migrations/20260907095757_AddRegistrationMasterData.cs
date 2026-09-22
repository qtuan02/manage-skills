using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_HEX_Record_Session_Identity",
                table: "HEX_ExamRecord");

            migrationBuilder.DropIndex(
                name: "UX_HEX_Record_Session_Patient",
                table: "HEX_ExamRecord");

            migrationBuilder.AddColumn<string>(
                name: "BloodAboCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BloodAboName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BloodRhCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BloodRhName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EthnicityCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EthnicityName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamLocationCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamLocationName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamReason",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "IdentityIssuedDate",
                table: "HEX_ExamRecord",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityIssuerCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IdentityIssuerName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InsuranceObjectCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InsuranceObjectName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "InsuranceValidFrom",
                table: "HEX_ExamRecord",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InsuranceValidTo",
                table: "HEX_ExamRecord",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OccupationCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OccupationName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PatientTypeCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PatientTypeName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentSourceCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentSourceName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentSourceOther",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProvinceCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProvinceName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RelativeFullName",
                table: "HEX_ExamRecord",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RelativeIdentityNumber",
                table: "HEX_ExamRecord",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RelativePhoneNumber",
                table: "HEX_ExamRecord",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RelativeRelationshipCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RelativeRelationshipName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WardCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WardName",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "HEX_MasterDataOption",
                columns: table => new
                {
                    OptionID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ParentCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
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
                    table.PrimaryKey("PK_HEX_MasterDataOption", x => x.OptionID);
                });

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Record_Session_Identity",
                table: "HEX_ExamRecord",
                columns: new[] { "SessionID", "IdentityNumber" },
                unique: true,
                filter: "\"IdentityNumber\" <> '' AND \"State\" NOT IN (4, 5)");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Record_Session_Patient",
                table: "HEX_ExamRecord",
                columns: new[] { "SessionID", "PatientCode" },
                unique: true,
                filter: "\"PatientCode\" <> '' AND \"State\" NOT IN (4, 5)");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_MasterDataOption_DivisionID_Category_Code",
                table: "HEX_MasterDataOption",
                columns: new[] { "DivisionID", "Category", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_MasterDataOption_DivisionID_Category_IsActive_OrderNo",
                table: "HEX_MasterDataOption",
                columns: new[] { "DivisionID", "Category", "IsActive", "OrderNo" });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_MasterDataOption_DivisionID_Category_ParentCode_IsActive",
                table: "HEX_MasterDataOption",
                columns: new[] { "DivisionID", "Category", "ParentCode", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_MasterDataOption");

            migrationBuilder.DropIndex(
                name: "UX_HEX_Record_Session_Identity",
                table: "HEX_ExamRecord");

            migrationBuilder.DropIndex(
                name: "UX_HEX_Record_Session_Patient",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "BloodAboCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "BloodAboName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "BloodRhCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "BloodRhName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "EthnicityCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "EthnicityName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "ExamLocationCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "ExamLocationName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "ExamReason",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "IdentityIssuedDate",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "IdentityIssuerCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "IdentityIssuerName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "InsuranceObjectCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "InsuranceObjectName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "InsuranceValidFrom",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "InsuranceValidTo",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "OccupationCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "OccupationName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "PatientTypeCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "PatientTypeName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "PaymentSourceCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "PaymentSourceName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "PaymentSourceOther",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "ProvinceCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "ProvinceName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "RelativeFullName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "RelativeIdentityNumber",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "RelativePhoneNumber",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "RelativeRelationshipCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "RelativeRelationshipName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "WardCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "WardName",
                table: "HEX_ExamRecord");

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
        }
    }
}

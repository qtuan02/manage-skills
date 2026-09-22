using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropRegistrationLegacyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""UX_HEX_Record_Session_Identity"";
                DROP INDEX IF EXISTS ""HEX_ExamRecord_SessionID_IdentityNumber_idx"";
                DROP INDEX IF EXISTS ""UX_HEX_Record_Session_Patient"";
                DROP INDEX IF EXISTS ""HEX_ExamRecord_SessionID_PatientCode_idx"";
            ");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Record_Session_PatientRef",
                table: "HEX_ExamRecord",
                columns: new[] { "SessionID", "PatientRefID" },
                unique: true,
                filter: "\"PatientRefID\" IS NOT NULL AND \"State\" NOT IN (4, 5)");

            migrationBuilder.DropColumn(
                name: "PatientID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "PatientCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "Dob",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "BirthYear",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "GenderID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "IdentityNumber",
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
                name: "PhoneNumber",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "EthnicityCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "EthnicityName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "OccupationCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "OccupationName",
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
                name: "InsuranceNumber",
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
                name: "StaffCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "OrgDeptName",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "JobTitle",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "RelativeRelationshipCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "RelativeRelationshipName",
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
                name: "ExamLocationCode",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "ExamLocationName",
                table: "HEX_ExamRecord");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_HEX_Record_Session_PatientRef",
                table: "HEX_ExamRecord");

            migrationBuilder.AddColumn<long>(
                name: "PatientID",
                table: "HEX_ExamRecord",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "PatientCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "Dob",
                table: "HEX_ExamRecord",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "BirthYear",
                table: "HEX_ExamRecord",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "GenderID",
                table: "HEX_ExamRecord",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "IdentityNumber",
                table: "HEX_ExamRecord",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
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
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IdentityIssuerName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "HEX_ExamRecord",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "HEX_ExamRecord",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "HEX_ExamRecord",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EthnicityCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EthnicityName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OccupationCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OccupationName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BloodAboCode",
                table: "HEX_ExamRecord",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BloodAboName",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BloodRhCode",
                table: "HEX_ExamRecord",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BloodRhName",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InsuranceNumber",
                table: "HEX_ExamRecord",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InsuranceObjectCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InsuranceObjectName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
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
                name: "StaffCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OrgDeptName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "JobTitle",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RelativeRelationshipCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RelativeRelationshipName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RelativeFullName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RelativeIdentityNumber",
                table: "HEX_ExamRecord",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RelativePhoneNumber",
                table: "HEX_ExamRecord",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PatientTypeCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PatientTypeName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentSourceCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentSourceName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamLocationCode",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamLocationName",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

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
        }
    }
}

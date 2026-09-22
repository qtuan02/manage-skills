using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationPlaceOption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RegistrationPlaceOptionID",
                table: "HEX_PatientInsurance",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_PatientInsurance_RegistrationPlaceOptionID",
                table: "HEX_PatientInsurance",
                column: "RegistrationPlaceOptionID");

            migrationBuilder.AddForeignKey(
                name: "FK_HEX_PatientInsurance_HEX_MasterDataOption_RegistrationPlace~",
                table: "HEX_PatientInsurance",
                column: "RegistrationPlaceOptionID",
                principalTable: "HEX_MasterDataOption",
                principalColumn: "OptionID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HEX_PatientInsurance_HEX_MasterDataOption_RegistrationPlace~",
                table: "HEX_PatientInsurance");

            migrationBuilder.DropIndex(
                name: "IX_HEX_PatientInsurance_RegistrationPlaceOptionID",
                table: "HEX_PatientInsurance");

            migrationBuilder.DropColumn(
                name: "RegistrationPlaceOptionID",
                table: "HEX_PatientInsurance");
        }
    }
}

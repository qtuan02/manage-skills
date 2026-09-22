using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignStepMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HEX_SignStepMap",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    VariantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SWStep = table.Column<int>(type: "integer", nullable: false),
                    ItemGroupID = table.Column<int>(type: "integer", nullable: true),
                    StepName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
                    SignTitle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
                    SWRoleID = table.Column<long>(type: "bigint", nullable: false),
                    SignType = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    SLType = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    SearchPattern = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    SLPage = table.Column<int>(type: "integer", nullable: false),
                    SLX = table.Column<float>(type: "real", nullable: false),
                    SLY = table.Column<float>(type: "real", nullable: false),
                    IsConclusionStep = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_SignStepMap", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamRecordSignStep_RecordID",
                table: "HEX_ExamRecordSignStep",
                column: "RecordID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_SignStepMap_DivisionID_VariantCode_ItemGroupID",
                table: "HEX_SignStepMap",
                columns: new[] { "DivisionID", "VariantCode", "ItemGroupID" },
                unique: true,
                filter: "\"ItemGroupID\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_SignStepMap_DivisionID_VariantCode_SWStep",
                table: "HEX_SignStepMap",
                columns: new[] { "DivisionID", "VariantCode", "SWStep" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_SignStepMap");

            migrationBuilder.DropIndex(
                name: "IX_HEX_ExamRecordSignStep_RecordID",
                table: "HEX_ExamRecordSignStep");
        }
    }
}

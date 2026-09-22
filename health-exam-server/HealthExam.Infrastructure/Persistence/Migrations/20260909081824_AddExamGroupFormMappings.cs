using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExamGroupFormMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HEX_ExamGroupFormMapping",
                columns: table => new
                {
                    MappingID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    VariantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TemplateCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_HEX_ExamGroupFormMapping", x => x.MappingID);
                });

            migrationBuilder.CreateTable(
                name: "HEX_ExamGroupFormSectionMapping",
                columns: table => new
                {
                    SectionMappingID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MappingID = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionKind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemGroupID = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ExamGroupFormSectionMapping", x => x.SectionMappingID);
                    table.ForeignKey(
                        name: "FK_HEX_ExamGroupFormSectionMapping_HEX_ExamGroupFormMapping_Ma~",
                        column: x => x.MappingID,
                        principalTable: "HEX_ExamGroupFormMapping",
                        principalColumn: "MappingID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "HEX_ExamGroupFormMapping",
                columns: new[] { "MappingID", "CreatedActorKind", "CreatedBy", "CreatedDate", "DivisionID", "IsActive", "ModifiedActorKind", "ModifiedBy", "ModifiedDate", "TemplateCode", "VariantCode" },
                values: new object[,]
                {
                    { new Guid("f0000001-0000-0000-0000-000000000001"), (short)1, 0L, new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc), "DEV", true, (short)1, 0L, new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc), "KSK-TREN18TUOI", "DTK_03" },
                    { new Guid("f0000001-0000-0000-0000-000000000004"), (short)1, 0L, new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc), "DHTESTING", true, (short)1, 0L, new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Utc), "KSK-TREN18TUOI", "DTK_03" }
                });

            migrationBuilder.InsertData(
                table: "HEX_ExamGroupFormSectionMapping",
                columns: new[] { "SectionMappingID", "ItemGroupID", "MappingID", "SectionKind" },
                values: new object[,]
                {
                    { new Guid("f0000001-0000-0000-0000-000000000002"), 64, new Guid("f0000001-0000-0000-0000-000000000001"), "HISTORY" },
                    { new Guid("f0000001-0000-0000-0000-000000000003"), 63, new Guid("f0000001-0000-0000-0000-000000000001"), "EXTRA_INFO" },
                    { new Guid("f0000001-0000-0000-0000-000000000005"), 64, new Guid("f0000001-0000-0000-0000-000000000004"), "HISTORY" },
                    { new Guid("f0000001-0000-0000-0000-000000000006"), 63, new Guid("f0000001-0000-0000-0000-000000000004"), "EXTRA_INFO" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamGroupFormMapping_DivisionID_VariantCode",
                table: "HEX_ExamGroupFormMapping",
                columns: new[] { "DivisionID", "VariantCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamGroupFormSectionMapping_MappingID_SectionKind",
                table: "HEX_ExamGroupFormSectionMapping",
                columns: new[] { "MappingID", "SectionKind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_ExamGroupFormSectionMapping");

            migrationBuilder.DropTable(
                name: "HEX_ExamGroupFormMapping");
        }
    }
}

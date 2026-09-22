using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExamRecordSignSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HEX_ExamRecordSignStep",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecordID = table.Column<Guid>(type: "uuid", nullable: false),
                    SWTID = table.Column<long>(type: "bigint", nullable: false),
                    FileDocTypeID = table.Column<int>(type: "integer", nullable: false),
                    SWStep = table.Column<int>(type: "integer", nullable: false),
                    StepName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    SWRoleID = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    SignedByEmployeeID = table.Column<long>(type: "bigint", nullable: true),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ExamRecordSignStep", x => x.ID);
                    table.ForeignKey(
                        name: "FK_HEX_ExamRecordSignStep_HEX_ExamRecord_RecordID",
                        column: x => x.RecordID,
                        principalTable: "HEX_ExamRecord",
                        principalColumn: "RecordID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_RecordSignStep_Status",
                table: "HEX_ExamRecordSignStep",
                columns: new[] { "DivisionID", "RecordID", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_HEX_RecordSignStep_Step",
                table: "HEX_ExamRecordSignStep",
                columns: new[] { "DivisionID", "RecordID", "SWTID", "SWStep" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_ExamRecordSignStep");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HEX_ImportBatch",
                columns: table => new
                {
                    BatchID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    SessionID = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: true, defaultValue: ""),
                    SheetName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, defaultValue: ""),
                    TotalRow = table.Column<int>(type: "integer", nullable: false),
                    SuccessRow = table.Column<int>(type: "integer", nullable: false),
                    ErrorRow = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, defaultValue: ""),
                    CreatedRecordCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedActorKind = table.Column<short>(type: "smallint", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedActorKind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ImportBatch", x => x.BatchID);
                    table.ForeignKey(
                        name: "FK_HEX_ImportBatch_HEX_ExamSession_SessionID",
                        column: x => x.SessionID,
                        principalTable: "HEX_ExamSession",
                        principalColumn: "SessionID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HEX_ImportBatchRow",
                columns: table => new
                {
                    ImportRowID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    BatchID = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNo = table.Column<int>(type: "integer", nullable: false),
                    RawData = table.Column<string>(type: "jsonb", nullable: false),
                    IsValid = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, defaultValue: ""),
                    RecordID = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ImportBatchRow", x => x.ImportRowID);
                    table.ForeignKey(
                        name: "FK_HEX_ImportBatchRow_HEX_ImportBatch_BatchID",
                        column: x => x.BatchID,
                        principalTable: "HEX_ImportBatch",
                        principalColumn: "BatchID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Import_Session",
                table: "HEX_ImportBatch",
                columns: new[] { "SessionID", "StartedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ImportRow_Error",
                table: "HEX_ImportBatchRow",
                columns: new[] { "BatchID", "RowNo" },
                filter: "NOT \"IsValid\"");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_ImportRow_Batch_RowNo",
                table: "HEX_ImportBatchRow",
                columns: new[] { "BatchID", "RowNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_ImportBatchRow");

            migrationBuilder.DropTable(
                name: "HEX_ImportBatch");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastEventAt",
                table: "HEX_ExamRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HEX_WebhookInbox",
                columns: table => new
                {
                    InboxID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    EventID = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    SubmissionID = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordID = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessState = table.Column<short>(type: "smallint", nullable: false),
                    RetryCount = table.Column<short>(type: "smallint", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, defaultValue: ""),
                    TraceID = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_WebhookInbox", x => x.InboxID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Inbox_Pending",
                table: "HEX_WebhookInbox",
                columns: new[] { "ProcessState", "ReceivedAt" },
                filter: "\"ProcessState\" IN (0, 2)");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Inbox_Event",
                table: "HEX_WebhookInbox",
                column: "EventID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_WebhookInbox");

            migrationBuilder.DropColumn(
                name: "LastEventAt",
                table: "HEX_ExamRecord");
        }
    }
}

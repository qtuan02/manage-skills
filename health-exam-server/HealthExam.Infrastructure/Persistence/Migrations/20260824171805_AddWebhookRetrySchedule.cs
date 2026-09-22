using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookRetrySchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HEX_Inbox_Pending",
                table: "HEX_WebhookInbox");

            migrationBuilder.DropIndex(
                name: "UX_HEX_Inbox_Event",
                table: "HEX_WebhookInbox");

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "HEX_WebhookInbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Inbox_Pending",
                table: "HEX_WebhookInbox",
                columns: new[] { "ProcessState", "NextAttemptAt", "ReceivedAt" },
                filter: "\"ProcessState\" IN (0, 2)");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Inbox_Event",
                table: "HEX_WebhookInbox",
                columns: new[] { "DivisionID", "EventID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HEX_Inbox_Pending",
                table: "HEX_WebhookInbox");

            migrationBuilder.DropIndex(
                name: "UX_HEX_Inbox_Event",
                table: "HEX_WebhookInbox");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "HEX_WebhookInbox");

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
    }
}

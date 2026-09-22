using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "HEX_ParaclinicalVendorLineNo");

            migrationBuilder.AddColumn<long>(
                name: "VendorLineNo",
                table: "HEX_ParaclinicalOrderItem",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HEX_IntegrationOutbox",
                columns: table => new
                {
                    OutboxID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    OrderID = table.Column<Guid>(type: "uuid", nullable: false),
                    Vendor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DedupKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    RetryCount = table.Column<short>(type: "smallint", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, defaultValue: ""),
                    ResponseSnippet = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, defaultValue: ""),
                    TraceID = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_IntegrationOutbox", x => x.OutboxID);
                });

            migrationBuilder.CreateIndex(
                name: "UX_HEX_OrderItem_VendorLineNo",
                table: "HEX_ParaclinicalOrderItem",
                columns: new[] { "DivisionID", "VendorLineNo" },
                unique: true,
                filter: "\"VendorLineNo\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Outbox_Order",
                table: "HEX_IntegrationOutbox",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Outbox_Pending",
                table: "HEX_IntegrationOutbox",
                columns: new[] { "State", "NextAttemptAt", "CreatedAt" },
                filter: "\"State\" IN (0, 2)");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Outbox_Dedup",
                table: "HEX_IntegrationOutbox",
                columns: new[] { "DivisionID", "DedupKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_IntegrationOutbox");

            migrationBuilder.DropIndex(
                name: "UX_HEX_OrderItem_VendorLineNo",
                table: "HEX_ParaclinicalOrderItem");

            migrationBuilder.DropColumn(
                name: "VendorLineNo",
                table: "HEX_ParaclinicalOrderItem");

            migrationBuilder.DropSequence(
                name: "HEX_ParaclinicalVendorLineNo");
        }
    }
}

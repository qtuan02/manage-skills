using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParaclinicalOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "HEX_ParaclinicalOrderNo");

            migrationBuilder.AddColumn<string>(
                name: "MessageID",
                table: "HEX_WebhookInbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HEX_ParaclinicalOrder",
                columns: table => new
                {
                    OrderID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    RecordID = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionID = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ParaclinicalKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true, defaultValue: ""),
                    SourcePackageID = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderedByID = table.Column<long>(type: "bigint", nullable: false),
                    OrderedByName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
                    OrderedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    RoomID = table.Column<int>(type: "integer", nullable: false),
                    TargetSystem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: "NONE"),
                    SentStatus = table.Column<short>(type: "smallint", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalOrderID = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, defaultValue: ""),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, defaultValue: ""),
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
                    table.PrimaryKey("PK_HEX_ParaclinicalOrder", x => x.OrderID);
                    table.ForeignKey(
                        name: "FK_HEX_ParaclinicalOrder_HEX_ExamRecord_RecordID",
                        column: x => x.RecordID,
                        principalTable: "HEX_ExamRecord",
                        principalColumn: "RecordID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HEX_ParaclinicalOrderItem",
                columns: table => new
                {
                    OrderItemID = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrderID = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordID = table.Column<Guid>(type: "uuid", nullable: false),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    ServiceID = table.Column<long>(type: "bigint", nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    ServiceName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    ServiceGroupCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    Quantity = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultSourceKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true, defaultValue: ""),
                    ResultRefID = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttachmentID = table.Column<Guid>(type: "uuid", nullable: true),
                    IsAbnormal = table.Column<bool>(type: "boolean", nullable: true),
                    MessageID = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, defaultValue: ""),
                    SourcePackageID = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedActorKind = table.Column<short>(type: "smallint", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedActorKind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ParaclinicalOrderItem", x => x.OrderItemID);
                    table.ForeignKey(
                        name: "FK_HEX_ParaclinicalOrderItem_HEX_ParaclinicalOrder_OrderID",
                        column: x => x.OrderID,
                        principalTable: "HEX_ParaclinicalOrder",
                        principalColumn: "OrderID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Inbox_MessageID",
                table: "HEX_WebhookInbox",
                columns: new[] { "DivisionID", "MessageID" },
                unique: true,
                filter: "\"MessageID\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Order_Record",
                table: "HEX_ParaclinicalOrder",
                columns: new[] { "RecordID", "OrderedAt" },
                descending: new[] { false, true },
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Order_Session",
                table: "HEX_ParaclinicalOrder",
                columns: new[] { "SessionID", "OrderedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Order_No",
                table: "HEX_ParaclinicalOrder",
                columns: new[] { "DivisionID", "OrderNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_OrderItem_Record",
                table: "HEX_ParaclinicalOrderItem",
                columns: new[] { "RecordID", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_HEX_OrderItem_MessageID",
                table: "HEX_ParaclinicalOrderItem",
                columns: new[] { "DivisionID", "MessageID" },
                unique: true,
                filter: "\"MessageID\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_OrderItem_Service",
                table: "HEX_ParaclinicalOrderItem",
                columns: new[] { "OrderID", "ServiceID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_ParaclinicalOrderItem");

            migrationBuilder.DropTable(
                name: "HEX_ParaclinicalOrder");

            migrationBuilder.DropIndex(
                name: "UX_HEX_Inbox_MessageID",
                table: "HEX_WebhookInbox");

            migrationBuilder.DropColumn(
                name: "MessageID",
                table: "HEX_WebhookInbox");

            migrationBuilder.DropSequence(
                name: "HEX_ParaclinicalOrderNo");
        }
    }
}

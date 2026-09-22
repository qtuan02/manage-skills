using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParaclinicalHisLinkageAndResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Alter HEX_ParaclinicalOrder: add HIS linkage & status columns
            migrationBuilder.AddColumn<long>(
                name: "HisPtId",
                table: "HEX_ParaclinicalOrder",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HisPtCode",
                table: "HEX_ParaclinicalOrder",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HisAdmissionId",
                table: "HEX_ParaclinicalOrder",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HisAdmissionCode",
                table: "HEX_ParaclinicalOrder",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HisTreatmentProcessId",
                table: "HEX_ParaclinicalOrder",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HisParaClinReqId",
                table: "HEX_ParaclinicalOrder",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "Status",
                table: "HEX_ParaclinicalOrder",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "HEX_ParaclinicalOrder",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "HEX_ParaclinicalOrder",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncAt",
                table: "HEX_ParaclinicalOrder",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncError",
                table: "HEX_ParaclinicalOrder",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "HEX_ParaclinicalOrder",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Order_HisParaClinReqId",
                table: "HEX_ParaclinicalOrder",
                columns: new[] { "DivisionID", "HisParaClinReqId" });

            // 2. Alter HEX_ParaclinicalOrderItem: add scheduling & HIS linkage columns
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "HEX_ParaclinicalOrderItem",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledFrom",
                table: "HEX_ParaclinicalOrderItem",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledTo",
                table: "HEX_ParaclinicalOrderItem",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HisParaClinReqDtlId",
                table: "HEX_ParaclinicalOrderItem",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HisParaClinProcessId",
                table: "HEX_ParaclinicalOrderItem",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ResultRequired",
                table: "HEX_ParaclinicalOrderItem",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_OrderItem_HisParaClinProcessId",
                table: "HEX_ParaclinicalOrderItem",
                columns: new[] { "DivisionID", "HisParaClinProcessId" });

            // 3. Create HEX_ParaclinicalResult table
            migrationBuilder.CreateTable(
                name: "HEX_ParaclinicalResult",
                columns: table => new
                {
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    HisResultId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResultDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ParaclinicalResult", x => x.ResultId);
                    table.ForeignKey(
                        name: "FK_HEX_ParaclinicalResult_HEX_ParaclinicalOrder_OrderId",
                        column: x => x.OrderId,
                        principalTable: "HEX_ParaclinicalOrder",
                        principalColumn: "OrderID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ParaclinicalResult_OrderId",
                table: "HEX_ParaclinicalResult",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_ParaclinicalResult_HisResultId",
                table: "HEX_ParaclinicalResult",
                columns: new[] { "DivisionID", "HisResultId" },
                unique: true);

            // 4. Create HEX_ParaclinicalResultItem table
            migrationBuilder.CreateTable(
                name: "HEX_ParaclinicalResultItem",
                columns: table => new
                {
                    ResultItemId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    HisDetailId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false, defaultValue: ""),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: ""),
                    ReferenceRange = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    AbnormalFlag = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: ""),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_ParaclinicalResultItem", x => x.ResultItemId);
                    table.ForeignKey(
                        name: "FK_HEX_ParaclinicalResultItem_HEX_ParaclinicalResult_ResultId",
                        column: x => x.ResultId,
                        principalTable: "HEX_ParaclinicalResult",
                        principalColumn: "ResultId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HEX_ParaclinicalResultItem_HEX_ParaclinicalOrderItem_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "HEX_ParaclinicalOrderItem",
                        principalColumn: "OrderItemID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ParaclinicalResultItem_ResultId",
                table: "HEX_ParaclinicalResultItem",
                column: "ResultId");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ParaclinicalResultItem_OrderItemId",
                table: "HEX_ParaclinicalResultItem",
                column: "OrderItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_ParaclinicalResultItem");

            migrationBuilder.DropTable(
                name: "HEX_ParaclinicalResult");

            migrationBuilder.DropIndex(
                name: "IX_HEX_OrderItem_HisParaClinProcessId",
                table: "HEX_ParaclinicalOrderItem");

            migrationBuilder.DropColumn(
                name: "ResultRequired",
                table: "HEX_ParaclinicalOrderItem");

            migrationBuilder.DropColumn(
                name: "HisParaClinProcessId",
                table: "HEX_ParaclinicalOrderItem");

            migrationBuilder.DropColumn(
                name: "HisParaClinReqDtlId",
                table: "HEX_ParaclinicalOrderItem");

            migrationBuilder.DropColumn(
                name: "ScheduledTo",
                table: "HEX_ParaclinicalOrderItem");

            migrationBuilder.DropColumn(
                name: "ScheduledFrom",
                table: "HEX_ParaclinicalOrderItem");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "HEX_ParaclinicalOrderItem");

            migrationBuilder.DropIndex(
                name: "IX_HEX_Order_HisParaClinReqId",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "LastSyncError",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "LastSyncAt",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "HisParaClinReqId",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "HisTreatmentProcessId",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "HisAdmissionCode",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "HisAdmissionId",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "HisPtCode",
                table: "HEX_ParaclinicalOrder");

            migrationBuilder.DropColumn(
                name: "HisPtId",
                table: "HEX_ParaclinicalOrder");
        }
    }
}

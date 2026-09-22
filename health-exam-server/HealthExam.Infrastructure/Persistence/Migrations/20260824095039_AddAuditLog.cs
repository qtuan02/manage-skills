using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HEX_AuditLog",
                columns: table => new
                {
                    AuditID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    DivisionID = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: ""),
                    EntityType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EntityID = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ActorID = table.Column<long>(type: "bigint", nullable: false),
                    ActorKind = table.Column<short>(type: "smallint", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, defaultValue: ""),
                    FromState = table.Column<short>(type: "smallint", nullable: true),
                    ToState = table.Column<short>(type: "smallint", nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: true),
                    TraceID = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: ""),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HEX_AuditLog", x => x.AuditID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Audit_Division",
                table: "HEX_AuditLog",
                columns: new[] { "DivisionID", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Audit_Entity",
                table: "HEX_AuditLog",
                columns: new[] { "EntityType", "EntityID", "OccurredAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HEX_AuditLog");
        }
    }
}

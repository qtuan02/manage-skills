using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SignStepMapDivisionRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SET NOT NULL vỡ nếu còn dòng NULL — tenant rỗng đã là giá trị mặc định của cột nên dồn về đó.
            migrationBuilder.Sql("UPDATE \"HEX_SignStepMap\" SET \"DivisionID\" = '' WHERE \"DivisionID\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "DivisionID",
                table: "HEX_SignStepMap",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldDefaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DivisionID",
                table: "HEX_SignStepMap",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "");
        }
    }
}

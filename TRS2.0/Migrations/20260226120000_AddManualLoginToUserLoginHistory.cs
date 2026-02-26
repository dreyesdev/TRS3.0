using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRS2._0.Migrations
{
    public partial class AddManualLoginToUserLoginHistory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ManualLogin",
                table: "UserLoginHistories",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManualLogin",
                table: "UserLoginHistories");
        }
    }
}

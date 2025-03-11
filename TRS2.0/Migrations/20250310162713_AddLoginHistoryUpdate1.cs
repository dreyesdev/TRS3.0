using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRS2._0.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginHistoryUpdate1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PersonId",
                table: "UserLoginHistories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginHistories_PersonId",
                table: "UserLoginHistories",
                column: "PersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserLoginHistories_personnel_PersonId",
                table: "UserLoginHistories",
                column: "PersonId",
                principalTable: "personnel",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserLoginHistories_personnel_PersonId",
                table: "UserLoginHistories");

            migrationBuilder.DropIndex(
                name: "IX_UserLoginHistories_PersonId",
                table: "UserLoginHistories");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "UserLoginHistories");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRS2._0.Migrations
{
    /// <inheritdoc />
    public partial class AddResponsibletoAffxPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AffxPersons_PersonId",
                table: "AffxPersons");

            

            migrationBuilder.AddColumn<int>(
                name: "ResponsibleId",
                table: "AffxPersons",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetErrorLogs_AuthorId",
                table: "TimesheetErrorLogs",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_AffxPersons_PersonId_Start_End",
                table: "AffxPersons",
                columns: new[] { "PersonId", "Start", "End" });

            migrationBuilder.CreateIndex(
                name: "IX_AffxPersons_ResponsibleId",
                table: "AffxPersons",
                column: "ResponsibleId");

            migrationBuilder.AddForeignKey(
                name: "FK_AffxPersons_personnel_ResponsibleId",
                table: "AffxPersons",
                column: "ResponsibleId",
                principalTable: "personnel",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TimesheetErrorLogs_AspNetUsers_AuthorId",
                table: "TimesheetErrorLogs",
                column: "AuthorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AffxPersons_personnel_ResponsibleId",
                table: "AffxPersons");

            migrationBuilder.DropForeignKey(
                name: "FK_TimesheetErrorLogs_AspNetUsers_AuthorId",
                table: "TimesheetErrorLogs");

            migrationBuilder.DropIndex(
                name: "IX_TimesheetErrorLogs_AuthorId",
                table: "TimesheetErrorLogs");

            migrationBuilder.DropIndex(
                name: "IX_AffxPersons_PersonId_Start_End",
                table: "AffxPersons");

            migrationBuilder.DropIndex(
                name: "IX_AffxPersons_ResponsibleId",
                table: "AffxPersons");

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "TimesheetErrorLogs");

            migrationBuilder.DropColumn(
                name: "ResponsibleId",
                table: "AffxPersons");

            migrationBuilder.CreateIndex(
                name: "IX_AffxPersons_PersonId",
                table: "AffxPersons",
                column: "PersonId");
        }
    }
}

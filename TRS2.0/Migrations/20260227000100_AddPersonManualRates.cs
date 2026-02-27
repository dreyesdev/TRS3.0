using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRS2._0.Migrations
{
    public partial class AddPersonManualRates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonManualRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    AffId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: false),
                    AnnualCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Dedication = table.Column<decimal>(type: "decimal(6,4)", nullable: false),
                    AnnualHours = table.Column<decimal>(type: "decimal(9,2)", nullable: false),
                    HourlyRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonManualRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonManualRates_Affiliations_AffId",
                        column: x => x.AffId,
                        principalTable: "Affiliations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PersonManualRates_personnel_PersonId",
                        column: x => x.PersonId,
                        principalTable: "personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonManualRates_AffId",
                table: "PersonManualRates",
                column: "AffId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonManualRates_PersonId",
                table: "PersonManualRates",
                column: "PersonId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonManualRates");
        }
    }
}

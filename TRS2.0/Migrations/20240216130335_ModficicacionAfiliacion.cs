using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRS2._0.Migrations
{
    /// <inheritdoc />
    public partial class ModficicacionAfiliacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "AffHours",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AffId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffHours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffHours_Affiliations_AffId",
                        column: x => x.AffId,
                        principalTable: "Affiliations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AffxPersons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    AffId = table.Column<int>(type: "int", nullable: false),
                    Start = table.Column<DateTime>(type: "datetime2", nullable: false),
                    End = table.Column<DateTime>(type: "datetime2", nullable: false)

                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffxPersons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffxPersons_Affiliations_AffId",
                        column: x => x.AffId,
                        principalTable: "Affiliations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AffxPersons_personnel_PersonId",
                        column: x => x.PersonId,
                        principalTable: "personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffHours_AffId",
                table: "AffHours",
                column: "AffId");


            migrationBuilder.CreateIndex(
                name: "IX_AffxPersons_AffId",
                table: "AffxPersons",
                column: "AffId");


            migrationBuilder.CreateIndex(
                name: "IX_AffxPersons_PersonId",
                table: "AffxPersons",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffHours");

            migrationBuilder.DropTable(
                name: "AffxPersons");


        }
    }
}

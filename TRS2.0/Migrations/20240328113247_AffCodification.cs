using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRS2._0.Migrations
{
    /// <inheritdoc />
    public partial class AffCodification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AffCodifications",
                columns: table => new
                {
                    Contract = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Dist = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Affiliation = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffCodifications", x => new { x.Contract, x.Dist });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffCodifications");
        }
    }
}

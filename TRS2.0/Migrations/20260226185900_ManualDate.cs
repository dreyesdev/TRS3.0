using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRS2._0.Migrations
{
    /// <inheritdoc />
    public partial class ManualDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ManualLoginDate",
                table: "UserLoginHistories",
                type: "date",
                nullable: true);

            
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {           
            migrationBuilder.DropColumn(
                name: "ManualLoginDate",
                table: "UserLoginHistories");
            
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleReturnReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "SaleReturns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reason",
                table: "SaleReturns");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseProductImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductImageContents",
                columns: table => new
                {
                    ProductImageId = table.Column<int>(type: "integer", nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImageContents", x => x.ProductImageId);
                    table.ForeignKey(
                        name: "FK_ProductImageContents_ProductImages_ProductImageId",
                        column: x => x.ProductImageId,
                        principalTable: "ProductImages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductImageContents");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccess.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameProductSkuToBarcode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Sku",
                table: "Products",
                newName: "Barcode");

            migrationBuilder.RenameIndex(
                name: "IX_Products_Sku",
                table: "Products",
                newName: "IX_Products_Barcode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Barcode",
                table: "Products",
                newName: "Sku");

            migrationBuilder.RenameIndex(
                name: "IX_Products_Barcode",
                table: "Products",
                newName: "IX_Products_Sku");
        }
    }
}

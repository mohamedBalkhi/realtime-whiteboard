using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealtimeWhiteboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveToDrawingElement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "DrawingElements",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "DrawingElements");
        }
    }
}

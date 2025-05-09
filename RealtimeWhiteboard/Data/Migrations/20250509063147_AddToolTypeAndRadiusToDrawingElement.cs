using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealtimeWhiteboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddToolTypeAndRadiusToDrawingElement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Type",
                table: "DrawingElements",
                newName: "ToolType");

            migrationBuilder.AddColumn<double>(
                name: "Radius",
                table: "DrawingElements",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Radius",
                table: "DrawingElements");

            migrationBuilder.RenameColumn(
                name: "ToolType",
                table: "DrawingElements",
                newName: "Type");
        }
    }
}

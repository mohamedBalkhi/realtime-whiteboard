using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealtimeWhiteboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStrokeDataJsonToDrawingElement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StrokeDataJson",
                table: "DrawingElements",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StrokeDataJson",
                table: "DrawingElements");
        }
    }
}

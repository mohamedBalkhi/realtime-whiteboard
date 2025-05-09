using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealtimeWhiteboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeIndexForSessionHistories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DrawingElements_Session_Active_Created",
                table: "DrawingElements",
                columns: new[] { "SessionId", "IsActive", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DrawingElements_Session_Active_Created",
                table: "DrawingElements");
        }
    }
}

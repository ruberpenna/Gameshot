using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameShot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNicknameUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Nickname",
                table: "AspNetUsers",
                column: "Nickname",
                unique: true,
                filter: "[Nickname] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_Nickname",
                table: "AspNetUsers");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MySocialApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_FeedIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Posts_UserId_CreatedAt",
                table: "Posts",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostMedia_MediaType_PostId",
                table: "PostMedia",
                columns: new[] { "MediaType", "PostId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_UserId_CreatedAt",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_PostMedia_MediaType_PostId",
                table: "PostMedia");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MySocialApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3_RealtimeChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_SenderId",
                table: "Messages");

            migrationBuilder.AddColumn<string>(
                name: "ClientMessageId",
                table: "Messages",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReadAt",
                table: "ConversationMembers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastReadMessageId",
                table: "ConversationMembers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderId_ClientMessageId",
                table: "Messages",
                columns: new[] { "SenderId", "ClientMessageId" },
                unique: true,
                filter: "[ClientMessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMembers_LastReadMessageId",
                table: "ConversationMembers",
                column: "LastReadMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationMembers_Messages_LastReadMessageId",
                table: "ConversationMembers",
                column: "LastReadMessageId",
                principalTable: "Messages",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConversationMembers_Messages_LastReadMessageId",
                table: "ConversationMembers");

            migrationBuilder.DropIndex(
                name: "IX_Messages_SenderId_ClientMessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_ConversationMembers_LastReadMessageId",
                table: "ConversationMembers");

            migrationBuilder.DropColumn(
                name: "ClientMessageId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "LastReadAt",
                table: "ConversationMembers");

            migrationBuilder.DropColumn(
                name: "LastReadMessageId",
                table: "ConversationMembers");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderId",
                table: "Messages",
                column: "SenderId");
        }
    }
}

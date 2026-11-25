using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentMessageBoard.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PostedById = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PostedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_messages_CreatedAtUtc",
                table: "messages",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_messages_Label_CreatedAtUtc",
                table: "messages",
                columns: new[] { "Label", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages");
        }
    }
}

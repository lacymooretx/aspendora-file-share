using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspendoraFileShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileRequestId",
                table: "ShareLinks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmitterEmail",
                table: "ShareLinks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmitterName",
                table: "ShareLinks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FileRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ShortId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    RecipientEmail = table.Column<string>(type: "text", nullable: true),
                    RecipientName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Closed = table.Column<bool>(type: "boolean", nullable: false),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_FileRequestId",
                table: "ShareLinks",
                column: "FileRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_ShortId",
                table: "FileRequests",
                column: "ShortId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_UserId",
                table: "FileRequests",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShareLinks_FileRequests_FileRequestId",
                table: "ShareLinks",
                column: "FileRequestId",
                principalTable: "FileRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShareLinks_FileRequests_FileRequestId",
                table: "ShareLinks");

            migrationBuilder.DropTable(
                name: "FileRequests");

            migrationBuilder.DropIndex(
                name: "IX_ShareLinks_FileRequestId",
                table: "ShareLinks");

            migrationBuilder.DropColumn(
                name: "FileRequestId",
                table: "ShareLinks");

            migrationBuilder.DropColumn(
                name: "SubmitterEmail",
                table: "ShareLinks");

            migrationBuilder.DropColumn(
                name: "SubmitterName",
                table: "ShareLinks");
        }
    }
}

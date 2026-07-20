using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentWriter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenamePublishTargetOAuthFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "web_posts",
                schema: "public");

            migrationBuilder.RenameColumn(
                name: "ApiKeyEnvVar",
                schema: "content_writer_v2",
                table: "PublishTargets",
                newName: "ClientSecretEnvVar");

            migrationBuilder.AddColumn<string>(
                name: "ClientIdEnvVar",
                schema: "content_writer_v2",
                table: "PublishTargets",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OAuthTokenEndpoint",
                schema: "content_writer_v2",
                table: "PublishTargets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientIdEnvVar",
                schema: "content_writer_v2",
                table: "PublishTargets");

            migrationBuilder.DropColumn(
                name: "OAuthTokenEndpoint",
                schema: "content_writer_v2",
                table: "PublishTargets");

            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.RenameColumn(
                name: "ClientSecretEnvVar",
                schema: "content_writer_v2",
                table: "PublishTargets",
                newName: "ApiKeyEnvVar");

            migrationBuilder.CreateTable(
                name: "web_posts",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Slug = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    content_structure = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_posts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_web_posts_Slug",
                schema: "public",
                table: "web_posts",
                column: "Slug",
                unique: true);
        }
    }
}

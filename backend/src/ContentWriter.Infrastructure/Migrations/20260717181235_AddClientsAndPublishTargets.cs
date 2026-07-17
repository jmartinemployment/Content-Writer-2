using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentWriter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientsAndPublishTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                schema: "content_writer_v2",
                table: "Projects",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Any pre-Client-model rows (only ever pre-launch smoke-test data) have no real
            // client to backfill against — drop them rather than leave an orphaned FK target.
            migrationBuilder.Sql(
                "DELETE FROM content_writer_v2.\"Projects\" WHERE \"ClientId\" = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.CreateTable(
                name: "Clients",
                schema: "content_writer_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublishTargets",
                schema: "content_writer_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeekBackendApiBaseUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ApiKeyEnvVar = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DefaultAuthorId = table.Column<int>(type: "integer", nullable: true),
                    CategoryStrategy = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishTargets_Clients_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "content_writer_v2",
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ClientId",
                schema: "content_writer_v2",
                table: "Projects",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishTargets_ClientId",
                schema: "content_writer_v2",
                table: "PublishTargets",
                column: "ClientId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Clients_ClientId",
                schema: "content_writer_v2",
                table: "Projects",
                column: "ClientId",
                principalSchema: "content_writer_v2",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Clients_ClientId",
                schema: "content_writer_v2",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "PublishTargets",
                schema: "content_writer_v2");

            migrationBuilder.DropTable(
                name: "Clients",
                schema: "content_writer_v2");

            migrationBuilder.DropIndex(
                name: "IX_Projects_ClientId",
                schema: "content_writer_v2",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ClientId",
                schema: "content_writer_v2",
                table: "Projects");
        }
    }
}

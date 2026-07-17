using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentWriter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewVerdicts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReviewVerdicts",
                schema: "content_writer_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    NotesJson = table.Column<string>(type: "text", nullable: false),
                    ReviewerProvider = table.Column<int>(type: "integer", nullable: false),
                    ReviewerModel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewVerdicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewVerdicts_GeneratedContents_GeneratedContentId",
                        column: x => x.GeneratedContentId,
                        principalSchema: "content_writer_v2",
                        principalTable: "GeneratedContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewVerdicts_GeneratedContentId",
                schema: "content_writer_v2",
                table: "ReviewVerdicts",
                column: "GeneratedContentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewVerdicts",
                schema: "content_writer_v2");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentWriter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBatchPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BatchJobItemSteps",
                schema: "content_writer_v2");

            migrationBuilder.DropTable(
                name: "BatchJobItems",
                schema: "content_writer_v2");

            migrationBuilder.DropTable(
                name: "BatchJobs",
                schema: "content_writer_v2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BatchJobs",
                schema: "content_writer_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedItems = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FailedItems = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalItems = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchJobs_Clients_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "content_writer_v2",
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BatchJobItems",
                schema: "content_writer_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorText = table.Column<string>(type: "text", nullable: true),
                    PreferredProvider = table.Column<int>(type: "integer", nullable: false),
                    ProjectUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TargetKeyword = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchJobItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchJobItems_BatchJobs_BatchJobId",
                        column: x => x.BatchJobId,
                        principalSchema: "content_writer_v2",
                        principalTable: "BatchJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BatchJobItems_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "content_writer_v2",
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BatchJobItemSteps",
                schema: "content_writer_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchJobItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorText = table.Column<string>(type: "text", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StepName = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchJobItemSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchJobItemSteps_BatchJobItems_BatchJobItemId",
                        column: x => x.BatchJobItemId,
                        principalSchema: "content_writer_v2",
                        principalTable: "BatchJobItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatchJobItems_BatchJobId",
                schema: "content_writer_v2",
                table: "BatchJobItems",
                column: "BatchJobId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchJobItems_ProjectId",
                schema: "content_writer_v2",
                table: "BatchJobItems",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchJobItemSteps_BatchJobItemId_StepName",
                schema: "content_writer_v2",
                table: "BatchJobItemSteps",
                columns: new[] { "BatchJobItemId", "StepName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchJobs_ClientId",
                schema: "content_writer_v2",
                table: "BatchJobs",
                column: "ClientId");
        }
    }
}

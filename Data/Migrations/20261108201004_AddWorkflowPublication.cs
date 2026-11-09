using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniWorkflowEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "WorkflowDefinitions",
                type: "datetimeoffset",
                nullable: true);

            // Önceden etkinleştirilmiş veya taleplerde kullanılmış tanımları da koru.
            migrationBuilder.Sql("""
                UPDATE w SET PublishedAt = w.CreatedAt
                FROM WorkflowDefinitions AS w
                WHERE w.IsActive = 1 OR EXISTS (
                    SELECT 1 FROM WorkflowRequests AS r WHERE r.WorkflowDefinitionId = w.Id
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "WorkflowDefinitions");
        }
    }
}

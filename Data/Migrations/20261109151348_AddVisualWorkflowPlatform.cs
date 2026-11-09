using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniWorkflowEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVisualWorkflowPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedUserId",
                table: "WorkflowSteps",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "WorkflowSteps",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GraphNodeId",
                table: "WorkflowSteps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FormValuesJson",
                table: "WorkflowRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequesterDepartmentId",
                table: "WorkflowRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GraphJson",
                table: "WorkflowDefinitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_AssignedUserId",
                table: "WorkflowSteps",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_DepartmentId",
                table: "WorkflowSteps",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_WorkflowDefinitionId_GraphNodeId",
                table: "WorkflowSteps",
                columns: new[] { "WorkflowDefinitionId", "GraphNodeId" },
                unique: true,
                filter: "[GraphNodeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRequests_RequesterDepartmentId",
                table: "WorkflowRequests",
                column: "RequesterDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DepartmentId",
                table: "AspNetUsers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Departments_DepartmentId",
                table: "AspNetUsers",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowRequests_Departments_RequesterDepartmentId",
                table: "WorkflowRequests",
                column: "RequesterDepartmentId",
                principalTable: "Departments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowSteps_AspNetUsers_AssignedUserId",
                table: "WorkflowSteps",
                column: "AssignedUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowSteps_Departments_DepartmentId",
                table: "WorkflowSteps",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Departments_DepartmentId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowRequests_Departments_RequesterDepartmentId",
                table: "WorkflowRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowSteps_AspNetUsers_AssignedUserId",
                table: "WorkflowSteps");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowSteps_Departments_DepartmentId",
                table: "WorkflowSteps");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowSteps_AssignedUserId",
                table: "WorkflowSteps");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowSteps_DepartmentId",
                table: "WorkflowSteps");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowSteps_WorkflowDefinitionId_GraphNodeId",
                table: "WorkflowSteps");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowRequests_RequesterDepartmentId",
                table: "WorkflowRequests");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_DepartmentId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "GraphNodeId",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "FormValuesJson",
                table: "WorkflowRequests");

            migrationBuilder.DropColumn(
                name: "RequesterDepartmentId",
                table: "WorkflowRequests");

            migrationBuilder.DropColumn(
                name: "GraphJson",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "AspNetUsers");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniWorkflowEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalDelegationAndAdministrationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdministrationEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministrationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdministrationEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDelegations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DelegatorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DelegateId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDelegations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDelegations_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalDelegations_AspNetUsers_DelegateId",
                        column: x => x.DelegateId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalDelegations_AspNetUsers_DelegatorId",
                        column: x => x.DelegatorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdministrationEvents_ActorId",
                table: "AdministrationEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDelegations_DelegateId_StartsOn_EndsOn",
                table: "ApprovalDelegations",
                columns: new[] { "DelegateId", "StartsOn", "EndsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDelegations_DelegatorId_StartsOn_EndsOn",
                table: "ApprovalDelegations",
                columns: new[] { "DelegatorId", "StartsOn", "EndsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDelegations_RoleId",
                table: "ApprovalDelegations",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdministrationEvents");

            migrationBuilder.DropTable(
                name: "ApprovalDelegations");
        }
    }
}

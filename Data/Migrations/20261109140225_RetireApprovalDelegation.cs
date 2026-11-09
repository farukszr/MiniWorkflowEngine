using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniWorkflowEngine.Data.Migrations
{
    public partial class RetireApprovalDelegation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Uygulama artık vekâlet tablosunu kullanmıyor. Eski kayıtları korumak için tabloyu bırak.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tablo korunduğu için önceki sürümde doğrudan yeniden kullanılabilir.
        }
    }
}

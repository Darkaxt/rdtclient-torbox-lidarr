using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RdtClient.Data.Data;

#nullable disable

namespace RdtClient.Data.Migrations;

[DbContext(typeof(DataContext))]
[Migration("20260511193500_Torrents_Add_Hash_Index")]
public partial class Torrents_Add_Hash_Index : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Torrents_Hash",
            table: "Torrents",
            column: "Hash");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Torrents_Hash",
            table: "Torrents");
    }
}

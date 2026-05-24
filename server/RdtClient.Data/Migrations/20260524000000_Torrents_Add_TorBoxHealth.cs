using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RdtClient.Data.Data;

#nullable disable

namespace RdtClient.Data.Migrations;

[DbContext(typeof(DataContext))]
[Migration("20260524000000_Torrents_Add_TorBoxHealth")]
public partial class Torrents_Add_TorBoxHealth : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "RdAvailability",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "RdPeers",
            table: "Torrents",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RdTracker",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RdTrackerMessage",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RdHealthSource",
            table: "Torrents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RdHealthUpdatedAt",
            table: "Torrents",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RdAvailability",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "RdPeers",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "RdTracker",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "RdTrackerMessage",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "RdHealthSource",
            table: "Torrents");

        migrationBuilder.DropColumn(
            name: "RdHealthUpdatedAt",
            table: "Torrents");
    }
}

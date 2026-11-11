using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Citra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Current Claude models choose their own sampling and refuse a "temperature", so bots no longer have one.
    /// Bots on Claude Haiku 4.5 move to Haiku 5.5: the old model does not accept the effort setting Citra now sends.
    /// </summary>
    public partial class RemoveBotTemperature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "temperature",
                table: "bots");

            migrationBuilder.Sql("UPDATE bots SET model = 'claude-haiku-5-5' WHERE model = 'claude-haiku-4-5';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "temperature",
                table: "bots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.2);
        }
    }
}

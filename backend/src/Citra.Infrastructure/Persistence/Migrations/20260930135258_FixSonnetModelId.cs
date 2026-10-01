using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Citra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only: "claude-sonnet-5" was never a real model id (it is "claude-sonnet-5-5"). Bots saved with it would
    /// fail to chat and could not be saved again (the settings form only offers supported ids).
    /// </summary>
    public partial class FixSonnetModelId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE bots SET model = 'claude-sonnet-5-5' WHERE model = 'claude-sonnet-5';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE bots SET model = 'claude-sonnet-5' WHERE model = 'claude-sonnet-5-5';");
        }
    }
}

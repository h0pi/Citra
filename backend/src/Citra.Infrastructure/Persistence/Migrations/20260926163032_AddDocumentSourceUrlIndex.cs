using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Citra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSourceUrlIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_documents_bot_id_source_url",
                table: "documents",
                columns: new[] { "bot_id", "source_url" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documents_bot_id_source_url",
                table: "documents");
        }
    }
}

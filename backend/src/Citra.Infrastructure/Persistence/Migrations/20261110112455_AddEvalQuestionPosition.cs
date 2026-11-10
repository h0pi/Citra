using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Citra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvalQuestionPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "position",
                table: "eval_questions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "position",
                table: "eval_questions");
        }
    }
}

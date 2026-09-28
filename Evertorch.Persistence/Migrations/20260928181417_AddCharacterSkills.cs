using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class AddCharacterSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_skills",
                columns: table => new
                {
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    skill_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_skills", x => new { x.character_id, x.skill_definition_id });
                    table.CheckConstraint("ck_character_skills_level", "level BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_character_skills_characters",
                        column: x => x.character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_skills");
        }
    }
}

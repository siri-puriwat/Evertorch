using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class AddSavedStatusEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_status_effects",
                columns: table => new
                {
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    status_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    remaining_ms = table.Column<int>(type: "integer", nullable: false),
                    str_percent = table.Column<int>(type: "integer", nullable: false),
                    agi_percent = table.Column<int>(type: "integer", nullable: false),
                    vit_percent = table.Column<int>(type: "integer", nullable: false),
                    int_percent = table.Column<int>(type: "integer", nullable: false),
                    dex_percent = table.Column<int>(type: "integer", nullable: false),
                    luk_percent = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_status_effects", x => new { x.character_id, x.status_definition_id });
                    table.CheckConstraint("ck_character_status_effects_agi_percent", "agi_percent BETWEEN -99 AND 1000");
                    table.CheckConstraint("ck_character_status_effects_dex_percent", "dex_percent BETWEEN -99 AND 1000");
                    table.CheckConstraint("ck_character_status_effects_int_percent", "int_percent BETWEEN -99 AND 1000");
                    table.CheckConstraint("ck_character_status_effects_luk_percent", "luk_percent BETWEEN -99 AND 1000");
                    table.CheckConstraint("ck_character_status_effects_remaining_ms", "remaining_ms BETWEEN 1 AND 86400000");
                    table.CheckConstraint("ck_character_status_effects_str_percent", "str_percent BETWEEN -99 AND 1000");
                    table.CheckConstraint("ck_character_status_effects_vit_percent", "vit_percent BETWEEN -99 AND 1000");
                    table.ForeignKey(
                        name: "fk_character_status_effects_characters",
                        column: x => x.character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_status_effects");
        }
    }
}

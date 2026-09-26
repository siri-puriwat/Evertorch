using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class AddQuestsAndCoins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.DropCheckConstraint(
                name: "ck_characters_resources",
                table: "characters");

            migrationBuilder.CreateTable(
                name: "character_quests",
                columns: table => new
                {
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    quest_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_quests", x => new { x.character_id, x.quest_definition_id });
                    table.CheckConstraint("ck_character_quests_progress", "progress >= 0");
                    table.CheckConstraint("ck_character_quests_state", "state IN ('active', 'completed')");
                    table.ForeignKey(
                        name: "fk_character_quests_characters",
                        column: x => x.character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_characters_resources",
                table: "characters",
                sql: "hp >= 0 AND sp >= 0 AND currency BETWEEN 0 AND 1000000000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_quests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.DropCheckConstraint(
                name: "ck_characters_resources",
                table: "characters");

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_characters_resources",
                table: "characters",
                sql: "hp >= 0 AND sp >= 0 AND currency >= 0");
        }
    }
}

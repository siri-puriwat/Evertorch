using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class AddRefining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_items_refine_level",
                table: "storage_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_items_refine_level",
                table: "inventory_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.CreateTable(
                name: "refine_attempts",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    item_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    item_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    level_before = table.Column<int>(type: "integer", nullable: false),
                    level_after = table.Column<int>(type: "integer", nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    material_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    material_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    material_quantity = table.Column<int>(type: "integer", nullable: false),
                    coins = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refine_attempts", x => x.operation_id);
                    table.CheckConstraint("ck_refine_attempts_coins", "coins BETWEEN 0 AND 1000000000");
                    table.CheckConstraint("ck_refine_attempts_levels", "level_before BETWEEN 0 AND 255 AND (level_after IS NULL) = (outcome = 'destroyed') AND (level_after BETWEEN 0 AND 255)");
                    table.CheckConstraint("ck_refine_attempts_material_quantity", "material_quantity BETWEEN 1 AND 1000000");
                    table.CheckConstraint("ck_refine_attempts_outcome", "outcome IN ('refined', 'destroyed', 'lowered', 'unchanged')");
                    table.ForeignKey(
                        name: "fk_refine_attempts_characters",
                        column: x => x.character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_items_refine_level",
                table: "storage_items",
                sql: "refine_level BETWEEN 0 AND 255");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_items_refine_level",
                table: "inventory_items",
                sql: "refine_level BETWEEN 0 AND 255");

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward', 'boss_reward', 'trade', 'storage_deposit', 'storage_withdraw', 'market_list', 'market_buy', 'market_collect', 'refine')");

            migrationBuilder.CreateIndex(
                name: "ix_refine_attempts_character_id",
                table: "refine_attempts",
                column: "character_id");

            migrationBuilder.Sql(RefineAttemptsImmutability.CreateSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refine_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_items_refine_level",
                table: "storage_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_items_refine_level",
                table: "inventory_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_items_refine_level",
                table: "storage_items",
                sql: "refine_level >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_items_refine_level",
                table: "inventory_items",
                sql: "refine_level >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward', 'boss_reward', 'trade', 'storage_deposit', 'storage_withdraw', 'market_list', 'market_buy', 'market_collect')");
        }
    }
}

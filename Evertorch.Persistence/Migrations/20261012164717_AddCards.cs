using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class AddCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.CreateTable(
                name: "card_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    change = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    card_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    item_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    item_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    card_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    fee = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_changes", x => x.id);
                    table.CheckConstraint("ck_card_changes_change", "change IN ('insert', 'remove', 'return')");
                    table.CheckConstraint("ck_card_changes_fee", "fee BETWEEN 0 AND 1000000");
                    table.CheckConstraint("ck_card_changes_slot", "slot BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_card_changes_characters",
                        column: x => x.character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_items_cards",
                table: "storage_items",
                sql: "instance_data_json IS NULL OR (quantity = 1 AND coalesce(jsonb_typeof(instance_data_json) = 'object', false) AND NOT coalesce(jsonb_path_exists(instance_data_json, 'strict $.keyvalue() ? (@.key != \"cards\")', '{}', true), true) AND coalesce(jsonb_path_exists(instance_data_json, 'strict $.cards ? (@.type() == \"array\" && @.size() >= 1 && @.size() <= 4)', '{}', true), false) AND NOT coalesce(jsonb_path_exists(instance_data_json, 'strict $.cards[*] ? (@.type() != \"string\")', '{}', true), true))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_items_cards",
                table: "inventory_items",
                sql: "instance_data_json IS NULL OR (quantity = 1 AND coalesce(jsonb_typeof(instance_data_json) = 'object', false) AND NOT coalesce(jsonb_path_exists(instance_data_json, 'strict $.keyvalue() ? (@.key != \"cards\")', '{}', true), true) AND coalesce(jsonb_path_exists(instance_data_json, 'strict $.cards ? (@.type() == \"array\" && @.size() >= 1 && @.size() <= 4)', '{}', true), false) AND NOT coalesce(jsonb_path_exists(instance_data_json, 'strict $.cards[*] ? (@.type() != \"string\")', '{}', true), true))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward', 'boss_reward', 'trade', 'storage_deposit', 'storage_withdraw', 'market_list', 'market_buy', 'market_collect', 'refine', 'card_insert', 'card_remove')");

            migrationBuilder.CreateIndex(
                name: "ix_card_changes_character_id",
                table: "card_changes",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_changes_operation_id",
                table: "card_changes",
                column: "operation_id");

            migrationBuilder.Sql(CardChangesImmutability.CreateSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_changes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_items_cards",
                table: "storage_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_items_cards",
                table: "inventory_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward', 'boss_reward', 'trade', 'storage_deposit', 'storage_withdraw', 'market_list', 'market_buy', 'market_collect', 'refine')");
        }
    }
}

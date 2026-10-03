using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class AddTradesAndStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.CreateTable(
                name: "account_storages",
                columns: table => new
                {
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_storages", x => x.account_id);
                    table.CheckConstraint("ck_account_storages_revision", "revision BETWEEN 0 AND 4294967295");
                    table.ForeignKey(
                        name: "fk_account_storages_accounts",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_character_id = table.Column<long>(type: "bigint", nullable: false),
                    second_character_id = table.Column<long>(type: "bigint", nullable: false),
                    committed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trades", x => x.id);
                    table.CheckConstraint("ck_trades_characters", "first_character_id < second_character_id");
                    table.ForeignKey(
                        name: "fk_trades_first_character",
                        column: x => x.first_character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trades_second_character",
                        column: x => x.second_character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "storage_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    item_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    refine_level = table.Column<int>(type: "integer", nullable: false),
                    instance_data_json = table.Column<string>(type: "jsonb", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_items", x => x.id);
                    table.CheckConstraint("ck_storage_items_quantity", "quantity BETWEEN 1 AND 1000000");
                    table.CheckConstraint("ck_storage_items_refine_level", "refine_level >= 0");
                    table.ForeignKey(
                        name: "fk_storage_items_account_storages",
                        column: x => x.account_id,
                        principalTable: "account_storages",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward', 'boss_reward', 'trade', 'storage_deposit', 'storage_withdraw')");

            migrationBuilder.CreateIndex(
                name: "ix_storage_items_account_id",
                table: "storage_items",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_trades_first_character_id",
                table: "trades",
                column: "first_character_id");

            migrationBuilder.CreateIndex(
                name: "ix_trades_second_character_id",
                table: "trades",
                column: "second_character_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_items");

            migrationBuilder.DropTable(
                name: "trades");

            migrationBuilder.DropTable(
                name: "account_storages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward', 'boss_reward')");
        }
    }
}

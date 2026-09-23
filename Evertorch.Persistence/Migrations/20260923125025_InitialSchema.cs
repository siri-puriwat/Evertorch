using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    login_normalized = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    password_scheme = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_status", "status IN ('active', 'disabled')");
                });

            migrationBuilder.CreateTable(
                name: "characters",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(23)", maxLength: 23, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(23)", maxLength: 23, nullable: false),
                    job_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    base_level = table.Column<int>(type: "integer", nullable: false),
                    job_level = table.Column<int>(type: "integer", nullable: false),
                    base_exp = table.Column<long>(type: "bigint", nullable: false),
                    job_exp = table.Column<long>(type: "bigint", nullable: false),
                    str = table.Column<int>(type: "integer", nullable: false),
                    agi = table.Column<int>(type: "integer", nullable: false),
                    vit = table.Column<int>(type: "integer", nullable: false),
                    @int = table.Column<int>(name: "int", type: "integer", nullable: false),
                    dex = table.Column<int>(type: "integer", nullable: false),
                    luk = table.Column<int>(type: "integer", nullable: false),
                    hp = table.Column<int>(type: "integer", nullable: false),
                    sp = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<long>(type: "bigint", nullable: false),
                    map_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position_x = table.Column<float>(type: "real", nullable: false),
                    position_y = table.Column<float>(type: "real", nullable: false),
                    position_z = table.Column<float>(type: "real", nullable: false),
                    inventory_revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_played_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_characters", x => x.id);
                    table.CheckConstraint("ck_characters_experience", "base_exp >= 0 AND job_exp >= 0");
                    table.CheckConstraint("ck_characters_inventory_revision", "inventory_revision BETWEEN 0 AND 4294967295");
                    table.CheckConstraint("ck_characters_levels", "base_level >= 1 AND job_level >= 1");
                    table.CheckConstraint("ck_characters_name", "name ~ '^[A-Za-z0-9]{4,23}$' AND name_normalized = lower(name)");
                    table.CheckConstraint("ck_characters_resources", "hp >= 0 AND sp >= 0 AND currency >= 0");
                    table.CheckConstraint("ck_characters_stats", "str >= 0 AND agi >= 0 AND vit >= 0 AND int >= 0 AND dex >= 0 AND luk >= 0");
                    table.ForeignKey(
                        name: "fk_characters_accounts",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "economy_ledger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_character_id = table.Column<long>(type: "bigint", nullable: true),
                    operation_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    item_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    item_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    quantity_delta = table.Column<int>(type: "integer", nullable: false),
                    currency_delta = table.Column<long>(type: "bigint", nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_economy_ledger", x => x.id);
                    table.CheckConstraint("ck_economy_ledger_operation_type", "operation_type IN ('pickup')");
                    table.ForeignKey(
                        name: "fk_economy_ledger_characters",
                        column: x => x.actor_character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    item_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    refine_level = table.Column<int>(type: "integer", nullable: false),
                    instance_data_json = table.Column<string>(type: "jsonb", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_items", x => x.id);
                    table.UniqueConstraint("ak_inventory_items_character_id_id", x => new { x.character_id, x.id });
                    table.CheckConstraint("ck_inventory_items_quantity", "quantity BETWEEN 1 AND 1000000");
                    table.CheckConstraint("ck_inventory_items_refine_level", "refine_level >= 0");
                    table.ForeignKey(
                        name: "fk_inventory_items_characters",
                        column: x => x.character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "equipment",
                columns: table => new
                {
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    slot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    inventory_item_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipment", x => new { x.character_id, x.slot });
                    table.CheckConstraint("ck_equipment_slot", "slot IN ('Weapon', 'Armor')");
                    table.ForeignKey(
                        name: "fk_equipment_characters",
                        column: x => x.character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_equipment_owned_inventory_item",
                        columns: x => new { x.character_id, x.inventory_item_id },
                        principalTable: "inventory_items",
                        principalColumns: new[] { "character_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_accounts_login_normalized",
                table: "accounts",
                column: "login_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_characters_account_id",
                table: "characters",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ux_characters_name_normalized",
                table: "characters",
                column: "name_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_economy_ledger_actor_character_id",
                table: "economy_ledger",
                column: "actor_character_id");

            migrationBuilder.CreateIndex(
                name: "ux_economy_ledger_operation_id",
                table: "economy_ledger",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_equipment_character_id_inventory_item_id",
                table: "equipment",
                columns: new[] { "character_id", "inventory_item_id" });

            migrationBuilder.CreateIndex(
                name: "ux_equipment_inventory_item_id",
                table: "equipment",
                column: "inventory_item_id",
                unique: true);

            migrationBuilder.Sql(LedgerImmutability.CreateSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(LedgerImmutability.DropSql);

            migrationBuilder.DropTable(
                name: "economy_ledger");

            migrationBuilder.DropTable(
                name: "equipment");

            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropTable(
                name: "characters");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class AddMarketBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.CreateTable(
                name: "market_listings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    seller_character_id = table.Column<long>(type: "bigint", nullable: false),
                    seller_account_id = table.Column<long>(type: "bigint", nullable: false),
                    item_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    units_listed = table.Column<int>(type: "integer", nullable: false),
                    units_left = table.Column<int>(type: "integer", nullable: false),
                    gross_coins = table.Column<long>(type: "bigint", nullable: false),
                    listed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_listings", x => x.id);
                    table.CheckConstraint("ck_market_listings_expiry", "expires_at > listed_at");
                    table.CheckConstraint("ck_market_listings_gross_coins", "gross_coins BETWEEN 0 AND 1000000000");
                    table.CheckConstraint("ck_market_listings_units", "units_listed BETWEEN 1 AND 1000000 AND units_left BETWEEN 0 AND units_listed");
                    table.ForeignKey(
                        name: "fk_market_listings_accounts",
                        column: x => x.seller_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_market_listings_characters",
                        column: x => x.seller_character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "market_prices",
                columns: table => new
                {
                    item_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    price = table.Column<int>(type: "integer", nullable: false),
                    listed_ms = table.Column<long>(type: "bigint", nullable: false),
                    settled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    stock_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rose_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_prices", x => x.item_definition_id);
                    table.CheckConstraint("ck_market_prices_listed_ms", "listed_ms >= 0");
                    table.CheckConstraint("ck_market_prices_price", "price BETWEEN 1 AND 1000000000");
                });

            migrationBuilder.CreateTable(
                name: "market_sales",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<long>(type: "bigint", nullable: false),
                    seller_character_id = table.Column<long>(type: "bigint", nullable: false),
                    buyer_character_id = table.Column<long>(type: "bigint", nullable: false),
                    item_definition_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    units = table.Column<int>(type: "integer", nullable: false),
                    gross_coins = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_sales", x => new { x.operation_id, x.listing_id });
                    table.CheckConstraint("ck_market_sales_gross_coins", "gross_coins BETWEEN 1 AND 1000000000");
                    table.CheckConstraint("ck_market_sales_units", "units BETWEEN 1 AND 1000000");
                    table.ForeignKey(
                        name: "fk_market_sales_buyer",
                        column: x => x.buyer_character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_market_sales_seller",
                        column: x => x.seller_character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward', 'boss_reward', 'trade', 'storage_deposit', 'storage_withdraw', 'market_list', 'market_buy', 'market_collect')");

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_seller_account_id",
                table: "market_listings",
                column: "seller_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_seller_character_id",
                table: "market_listings",
                column: "seller_character_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_stock",
                table: "market_listings",
                columns: new[] { "item_definition_id", "listed_at", "id" },
                filter: "units_left > 0");

            migrationBuilder.CreateIndex(
                name: "ix_market_sales_buyer_character_id",
                table: "market_sales",
                column: "buyer_character_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_sales_seller_character_id",
                table: "market_sales",
                column: "seller_character_id");

            migrationBuilder.Sql(MarketSalesImmutability.CreateSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_listings");

            migrationBuilder.DropTable(
                name: "market_prices");

            migrationBuilder.DropTable(
                name: "market_sales");

            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward', 'boss_reward', 'trade', 'storage_deposit', 'storage_withdraw')");

            migrationBuilder.Sql(MarketSalesImmutability.DropSql);
        }
    }
}

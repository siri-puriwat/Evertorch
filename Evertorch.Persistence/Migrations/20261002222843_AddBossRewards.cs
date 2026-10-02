using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class AddBossRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward', 'boss_reward')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_economy_ledger_operation_type",
                table: "economy_ledger",
                sql: "operation_type IN ('pickup', 'equip', 'unequip', 'consume', 'buy', 'sell', 'quest_reward')");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class ExpandEquipmentSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_equipment_slot",
                table: "equipment");

            migrationBuilder.AddCheckConstraint(
                name: "ck_equipment_slot",
                table: "equipment",
                sql: "slot IN ('Weapon', 'Armor', 'Headgear', 'Shield', 'Garment', 'Footwear', 'Accessory1', 'Accessory2')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_equipment_slot",
                table: "equipment");

            migrationBuilder.AddCheckConstraint(
                name: "ck_equipment_slot",
                table: "equipment",
                sql: "slot IN ('Weapon', 'Armor')");
        }
    }
}

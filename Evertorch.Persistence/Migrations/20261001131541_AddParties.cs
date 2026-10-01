using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class AddParties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parties",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    leader_character_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parties", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "party_members",
                columns: table => new
                {
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    party_id = table.Column<long>(type: "bigint", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    join_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_members", x => x.character_id);
                    table.CheckConstraint("ck_party_members_join_order", "join_order >= 1");
                    table.ForeignKey(
                        name: "fk_party_members_characters",
                        column: x => x.character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_party_members_parties",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_party_members_party_id_character_id",
                table: "party_members",
                columns: new[] { "party_id", "character_id" },
                unique: true);

            // The leader is one of the members (Persistence §4). Outside the EF model, which would see a cycle, and
            // checked at the commit, so a party and its first members are written in one transaction.
            migrationBuilder.Sql(
                "ALTER TABLE parties ADD CONSTRAINT fk_parties_leader_member FOREIGN KEY (id, leader_character_id) "
                + "REFERENCES party_members (party_id, character_id) DEFERRABLE INITIALLY DEFERRED;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE parties DROP CONSTRAINT fk_parties_leader_member;");

            migrationBuilder.DropTable(
                name: "party_members");

            migrationBuilder.DropTable(
                name: "parties");
        }
    }
}

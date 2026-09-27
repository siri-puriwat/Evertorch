using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Evertorch.Persistence
{
    /// <inheritdoc />
    public partial class AddSessionTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "session_tokens",
                columns: table => new
                {
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_tokens", x => x.token_hash);
                    table.CheckConstraint("ck_session_tokens_expiry", "expires_at > issued_at");
                    table.CheckConstraint("ck_session_tokens_token_hash", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_session_tokens_accounts",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_password",
                table: "accounts",
                sql: "(password_hash IS NULL) = (password_scheme IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_session_tokens_account_id",
                table: "session_tokens",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_tokens_expires_at",
                table: "session_tokens",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_tokens");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_password",
                table: "accounts");
        }
    }
}

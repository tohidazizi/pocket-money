using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketMoney.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M001_InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    actor_type = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    details_json = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "households",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    default_currency_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_households", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ip_bans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_address = table.Column<string>(type: "text", nullable: false),
                    ban_count = table.Column<int>(type: "integer", nullable: false),
                    banned_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ip_bans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "login_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    ip_address = table.Column<string>(type: "text", nullable: false),
                    http_request_info = table.Column<string>(type: "text", nullable: false),
                    is_successful = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parents",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: false),
                    parent_pin_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parents", x => x.id);
                    table.ForeignKey(
                        name: "fk_parents_households_household_id",
                        column: x => x.household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "children",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pin_hash = table.Column<string>(type: "text", nullable: false),
                    currency_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    current_balance = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false, defaultValue: 0.000m),
                    creator_id = table.Column<string>(type: "text", nullable: false),
                    unsuccessful_login_attempts = table.Column<byte>(type: "smallint", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    security_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_children", x => x.id);
                    table.ForeignKey(
                        name: "fk_children_households_household_id",
                        column: x => x.household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_children_parents_creator_id",
                        column: x => x.creator_id,
                        principalTable: "parents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "household_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_email = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    invited_by_parent_id = table.Column<string>(type: "text", nullable: false),
                    is_accepted = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_household_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_household_invitations_households_household_id",
                        column: x => x.household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_household_invitations_parents_invited_by_parent_id",
                        column: x => x.invited_by_parent_id,
                        principalTable: "parents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    currency_key = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(13,3)", precision: 13, scale: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    remaining_after = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    creator_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_transactions_children_child_id",
                        column: x => x.child_id,
                        principalTable: "children",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transactions_households_household_id",
                        column: x => x.household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transactions_parents_creator_id",
                        column: x => x.creator_id,
                        principalTable: "parents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_household_id",
                table: "audit_logs",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "ix_children_account_id",
                table: "children",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_children_creator_id",
                table: "children",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_children_household_id",
                table: "children",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "ix_household_invitations_household_id",
                table: "household_invitations",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "ix_household_invitations_invited_by_parent_id",
                table: "household_invitations",
                column: "invited_by_parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_ip_bans_ip_address",
                table: "ip_bans",
                column: "ip_address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_login_attempts_ip_address_is_successful_created_at",
                table: "login_attempts",
                columns: new[] { "ip_address", "is_successful", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parents_household_id",
                table: "parents",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "ix_parents_id_household_id",
                table: "parents",
                columns: new[] { "id", "household_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_child_id_created_at_id",
                table: "transactions",
                columns: new[] { "child_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_creator_id",
                table: "transactions",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_household_id",
                table: "transactions",
                column: "household_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "household_invitations");

            migrationBuilder.DropTable(
                name: "ip_bans");

            migrationBuilder.DropTable(
                name: "login_attempts");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "children");

            migrationBuilder.DropTable(
                name: "parents");

            migrationBuilder.DropTable(
                name: "households");
        }
    }
}

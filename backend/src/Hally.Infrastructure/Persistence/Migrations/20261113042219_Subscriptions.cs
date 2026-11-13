using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hally.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Subscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    product_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    store_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    purchased_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    will_renew = table.Column<bool>(type: "boolean", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trial_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trial_started_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscriptions_families_family_id",
                        column: x => x.family_id,
                        principalTable: "families",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_expires_at",
                table: "subscriptions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_family_id",
                table: "subscriptions",
                column: "family_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_source_store_reference",
                table: "subscriptions",
                columns: new[] { "source", "store_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_trial_started_by_user_id",
                table: "subscriptions",
                column: "trial_started_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscriptions");
        }
    }
}

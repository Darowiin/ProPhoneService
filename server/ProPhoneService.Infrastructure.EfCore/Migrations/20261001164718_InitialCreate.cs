using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProPhoneService.Infrastructure.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    manufacturer = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    serial_number = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review", x => x.id);
                    table.CheckConstraint("CK_review_rating_range", "\"rating\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_review_client_client_id",
                        column: x => x.client_id,
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repair_order",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    total_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_order", x => x.id);
                    table.ForeignKey(
                        name: "FK_repair_order_client_client_id",
                        column: x => x.client_id,
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_repair_order_device_device_id",
                        column: x => x.device_id,
                        principalTable: "device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repair_order_service",
                columns: table => new
                {
                    repair_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_order_service", x => new { x.repair_order_id, x.service_id });
                    table.ForeignKey(
                        name: "FK_repair_order_service_repair_order_repair_order_id",
                        column: x => x.repair_order_id,
                        principalTable: "repair_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_repair_order_service_service_service_id",
                        column: x => x.service_id,
                        principalTable: "service",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repair_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repair_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_repair_status_history_repair_order_repair_order_id",
                        column: x => x.repair_order_id,
                        principalTable: "repair_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_email",
                table: "client",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_phone",
                table: "client",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_repair_order_client_id",
                table: "repair_order",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "IX_repair_order_device_id",
                table: "repair_order",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "IX_repair_order_status",
                table: "repair_order",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_repair_order_service_service_id",
                table: "repair_order_service",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "IX_repair_status_history_changed_at",
                table: "repair_status_history",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "IX_repair_status_history_repair_order_id",
                table: "repair_status_history",
                column: "repair_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_review_client_id",
                table: "review",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "IX_review_created_at",
                table: "review",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_service_is_active",
                table: "service",
                column: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repair_order_service");

            migrationBuilder.DropTable(
                name: "repair_status_history");

            migrationBuilder.DropTable(
                name: "review");

            migrationBuilder.DropTable(
                name: "service");

            migrationBuilder.DropTable(
                name: "repair_order");

            migrationBuilder.DropTable(
                name: "client");

            migrationBuilder.DropTable(
                name: "device");
        }
    }
}

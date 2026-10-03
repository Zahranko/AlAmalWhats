using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaPlatform.Api.Migrations
{
    /// <inheritdoc />
    public partial class Messaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "error",
                table: "webhook_events",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    prefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    key_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_by_id = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    file_number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    opted_out = table.Column<bool>(type: "bit", nullable: false),
                    opted_out_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_inbound_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_message_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media_files",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    meta_media_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    meta_uploaded_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    uploaded_by_id = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    meta_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    parameter_format = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    components_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    synced_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "campaigns",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    template_id = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    total_recipients = table.Column<int>(type: "int", nullable: false),
                    created_by_id = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaigns", x => x.id);
                    table.ForeignKey(
                        name: "FK_campaigns_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_campaigns_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    direction = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    customer_id = table.Column<int>(type: "int", nullable: true),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    template_id = table.Column<int>(type: "int", nullable: true),
                    template_name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    params_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    media_file_id = table.Column<long>(type: "bigint", nullable: true),
                    inbound_media_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    media_file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    media_content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    error_code = table.Column<int>(type: "int", nullable: true),
                    wa_message_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    campaign_id = table.Column<int>(type: "int", nullable: true),
                    sent_by_id = table.Column<int>(type: "int", nullable: true),
                    api_key_id = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    delivered_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    read_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    pricing_category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    billable = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_messages_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_messages_media_files_media_file_id",
                        column: x => x.media_file_id,
                        principalTable: "media_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_messages_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_messages_users_sent_by_id",
                        column: x => x.sent_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_key_hash",
                table: "api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_created_at",
                table: "campaigns",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_created_by_id",
                table: "campaigns",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_template_id",
                table: "campaigns",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "IX_customers_file_number",
                table: "customers",
                column: "file_number");

            migrationBuilder.CreateIndex(
                name: "IX_customers_name",
                table: "customers",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_customers_phone",
                table: "customers",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_files_created_at",
                table: "media_files",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_messages_campaign_id",
                table: "messages",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_created_at",
                table: "messages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_messages_customer_id_created_at",
                table: "messages",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_media_file_id",
                table: "messages",
                column: "media_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_sent_by_id",
                table: "messages",
                column: "sent_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_status_next_attempt_at",
                table: "messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_template_id",
                table: "messages",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_wa_message_id",
                table: "messages",
                column: "wa_message_id",
                unique: true,
                filter: "[wa_message_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_templates_meta_id",
                table: "templates",
                column: "meta_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_templates_name_language",
                table: "templates",
                columns: new[] { "name", "language" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "media_files");

            migrationBuilder.DropTable(
                name: "templates");

            migrationBuilder.DropColumn(
                name: "error",
                table: "webhook_events");
        }
    }
}

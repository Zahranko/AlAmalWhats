using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaPlatform.Api.Migrations
{
    /// <inheritdoc />
    public partial class LastReadAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_read_at",
                table: "customers",
                type: "datetime2",
                nullable: true);

            // Conversations already answered count as read; unanswered ones start unread.
            migrationBuilder.Sql(
                "UPDATE customers SET last_read_at = last_inbound_at WHERE last_message_at >= last_inbound_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_read_at",
                table: "customers");
        }
    }
}

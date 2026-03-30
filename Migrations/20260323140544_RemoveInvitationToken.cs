using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace identityServer.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInvitationToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectInvitations_Token",
                table: "ProjectInvitations");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "ProjectInvitations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "ProjectInvitations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_Token",
                table: "ProjectInvitations",
                column: "Token",
                unique: true);
        }
    }
}

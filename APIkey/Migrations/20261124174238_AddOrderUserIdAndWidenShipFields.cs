using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APIkey.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderUserIdAndWidenShipFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ShipName",
                table: "orders",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(40)",
                oldMaxLength: 40,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "ShipCountry",
                table: "orders",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(15)",
                oldMaxLength: 15,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "ShipCity",
                table: "orders",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(15)",
                oldMaxLength: 15,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "ShipAddress",
                table: "orders",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(60)",
                oldMaxLength: 60,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "UserID",
                table: "orders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_UserID",
                table: "orders",
                column: "UserID");

            migrationBuilder.AddForeignKey(
                name: "orders_ibfk_user",
                table: "orders",
                column: "UserID",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "orders_ibfk_user",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_UserID",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "UserID",
                table: "orders");

            migrationBuilder.AlterColumn<string>(
                name: "ShipName",
                table: "orders",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(80)",
                oldMaxLength: 80,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "ShipCountry",
                table: "orders",
                type: "varchar(15)",
                maxLength: 15,
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(60)",
                oldMaxLength: 60,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "ShipCity",
                table: "orders",
                type: "varchar(15)",
                maxLength: 15,
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(60)",
                oldMaxLength: 60,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "ShipAddress",
                table: "orders",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(200)",
                oldMaxLength: 200,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");
        }
    }
}

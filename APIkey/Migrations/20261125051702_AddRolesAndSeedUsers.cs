using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace APIkey.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndSeedUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoleId",
                table: "users",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, collation: "utf8mb4_0900_ai_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_ai_ci");

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "ADMIN" },
                    { 2, "EMPLEADO" },
                    { 3, "CLIENTE" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_RoleId",
                table: "users",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_roles_Name",
                table: "roles",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "users_ibfk_role",
                table: "users",
                column: "RoleId",
                principalTable: "roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Sembrar 1 administrador (admin) y 2 empleados (Robe, Leo) con contraseñas PBKDF2-SHA256 (100,000 iteraciones)
            // Contraseñas por defecto: admin -> Admin1234!, Robe -> Robe1234!, Leo -> Leo1234!
            migrationBuilder.Sql(@"
                INSERT INTO users (Username, Email, PasswordHash, RoleId, CreatedAt, IsActive)
                SELECT 'admin', 'admin@northwind.local', 'D44AB5A1A63A49A040A4FF637A476D83:CBE56ABD9D98B942CD72A9E6D430F6F27450D8E4A33ECBEADC48E607BE31879F', 1, UTC_TIMESTAMP(), 1
                WHERE NOT EXISTS (SELECT 1 FROM users WHERE LOWER(Username) = 'admin' OR LOWER(Email) = 'admin@northwind.local');
                UPDATE users SET RoleId = 1, IsActive = 1 WHERE LOWER(Username) = 'admin';

                INSERT INTO users (Username, Email, PasswordHash, RoleId, CreatedAt, IsActive)
                SELECT 'Robe', 'robe@northwind.local', 'F75F6B2D7F1D9F727BF9354AF8AC7AC7:494B583AF26FC717C80313E2AD924BE6C632A92D02039E0EDF4C747C6DD7879D', 2, UTC_TIMESTAMP(), 1
                WHERE NOT EXISTS (SELECT 1 FROM users WHERE LOWER(Username) = 'robe' OR LOWER(Email) = 'robe@northwind.local');
                UPDATE users SET RoleId = 2, IsActive = 1 WHERE LOWER(Username) = 'robe';

                INSERT INTO users (Username, Email, PasswordHash, RoleId, CreatedAt, IsActive)
                SELECT 'Leo', 'leo@northwind.local', '7E3580C70EC96A39B8CAC7D83CDC19B1:7A5B93C1AB127F3904313AE445F248E605D937C84BA544A37AE9D17CAAA83D15', 2, UTC_TIMESTAMP(), 1
                WHERE NOT EXISTS (SELECT 1 FROM users WHERE LOWER(Username) = 'leo' OR LOWER(Email) = 'leo@northwind.local');
                UPDATE users SET RoleId = 2, IsActive = 1 WHERE LOWER(Username) = 'leo';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM users WHERE LOWER(Email) IN ('admin@northwind.local', 'robe@northwind.local', 'leo@northwind.local');
            ");

            migrationBuilder.DropForeignKey(
                name: "users_ibfk_role",
                table: "users");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropIndex(
                name: "IX_users_RoleId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "users");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameShot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponsAndPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CouponDiscount",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CouponName",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PointsEarned",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PointsUsed",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserCouponId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PointBalance",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DiscountType = table.Column<int>(type: "int", nullable: false),
                    DiscountValue = table.Column<int>(type: "int", nullable: false),
                    MaxDiscount = table.Column<int>(type: "int", nullable: true),
                    MinOrderAmount = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IssueLimit = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                    table.CheckConstraint("CK_Coupons_DiscountValue", "[DiscountValue] > 0");
                    table.CheckConstraint("CK_Coupons_Percent", "[DiscountType] <> 1 OR [DiscountValue] <= 100");
                });

            migrationBuilder.CreateTable(
                name: "PointTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    BalanceAfter = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PointTransactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserCoupons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CouponId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedOrderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCoupons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCoupons_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCoupons_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AspNetUsers_PointBalance",
                table: "AspNetUsers",
                sql: "[PointBalance] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_Code",
                table: "Coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PointTransactions_UserId_CreatedAt",
                table: "PointTransactions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCoupons_CouponId_UserId",
                table: "UserCoupons",
                columns: new[] { "CouponId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCoupons_UserId_UsedAt",
                table: "UserCoupons",
                columns: new[] { "UserId", "UsedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PointTransactions");

            migrationBuilder.DropTable(
                name: "UserCoupons");

            migrationBuilder.DropTable(
                name: "Coupons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AspNetUsers_PointBalance",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CouponDiscount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CouponName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PointsEarned",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PointsUsed",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UserCouponId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PointBalance",
                table: "AspNetUsers");
        }
    }
}

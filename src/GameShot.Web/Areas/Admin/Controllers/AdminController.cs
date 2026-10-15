using GameShot.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Areas.Admin.Controllers;

/// <summary>관리자 화면 공통: Admin 역할만, 모든 POST에 CSRF 검사</summary>
[Area("Admin")]
[Authorize(Roles = AdminBootstrap.AdminRole)]
[AutoValidateAntiforgeryToken]
public abstract class AdminController : Controller
{
    protected void Flash(bool success, string message) =>
        TempData[success ? "FlashSuccess" : "FlashError"] = message;
}

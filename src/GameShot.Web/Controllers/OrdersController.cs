using GameShot.Web.Models;
using GameShot.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Controllers;

[Authorize]
[Route("orders")]
[AutoValidateAntiforgeryToken]
public class OrdersController(OrderService orders, UserManager<ApplicationUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await orders.ListForUserAsync(UserId, ct));

    [HttpGet("{orderNumber}")]
    public async Task<IActionResult> Details(string orderNumber, CancellationToken ct)
    {
        var order = await orders.FindForUserAsync(UserId, orderNumber, ct);
        return order is null ? NotFound() : View(order);
    }

    /// <summary>가상 결제 화면. 실제 PG 대신 성공/실패를 고른다.</summary>
    [HttpGet("{orderNumber}/pay")]
    public async Task<IActionResult> Pay(string orderNumber, CancellationToken ct)
    {
        var order = await orders.FindForUserAsync(UserId, orderNumber, ct);
        if (order is null)
        {
            return NotFound();
        }
        if (!order.CanPay)
        {
            return RedirectToAction(nameof(Details), new { orderNumber });
        }
        return View(order);
    }

    [HttpPost("{orderNumber}/pay")]
    public async Task<IActionResult> Pay(string orderNumber, bool approve, CancellationToken ct)
    {
        var result = approve
            ? await orders.CompletePaymentAsync(UserId, orderNumber, ct)
            : await orders.FailPaymentAsync(UserId, orderNumber, ct);

        if (result.Success && approve)
        {
            return RedirectToAction(nameof(Complete), new { orderNumber });
        }

        TempData[result.Success ? "FlashSuccess" : "FlashError"] = result.Message;
        return RedirectToAction(nameof(Details), new { orderNumber });
    }

    [HttpGet("{orderNumber}/complete")]
    public async Task<IActionResult> Complete(string orderNumber, CancellationToken ct)
    {
        var order = await orders.FindForUserAsync(UserId, orderNumber, ct);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost("{orderNumber}/cancel")]
    public async Task<IActionResult> Cancel(string orderNumber, CancellationToken ct)
    {
        var result = await orders.CancelAsync(UserId, orderNumber, ct);
        TempData[result.Success ? "FlashSuccess" : "FlashError"] = result.Message;
        return RedirectToAction(nameof(Details), new { orderNumber });
    }

    private string UserId => users.GetUserId(User)!;
}

using GameShot.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameShot.Web.Areas.Admin.Controllers;

[Route("admin/orders")]
public class OrdersController(OrderService orders) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] AdminOrderFilter filter, CancellationToken ct)
    {
        ViewData["Filter"] = filter;
        return View(await orders.SearchForAdminAsync(filter, ct));
    }

    [HttpGet("{orderNumber}")]
    public async Task<IActionResult> Details(string orderNumber, CancellationToken ct)
    {
        var order = await orders.FindForAdminAsync(orderNumber, ct);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost("{orderNumber}/prepare")]
    public async Task<IActionResult> Prepare(string orderNumber, CancellationToken ct) =>
        Done(await orders.StartPreparingAsync(orderNumber, ct), orderNumber);

    [HttpPost("{orderNumber}/ship")]
    public async Task<IActionResult> Ship(string orderNumber, string? courier, string? trackingNumber, CancellationToken ct) =>
        Done(await orders.ShipAsync(orderNumber, courier, trackingNumber, ct), orderNumber);

    [HttpPost("{orderNumber}/deliver")]
    public async Task<IActionResult> Deliver(string orderNumber, CancellationToken ct) =>
        Done(await orders.MarkDeliveredAsync(orderNumber, ct), orderNumber);

    [HttpPost("{orderNumber}/cancel")]
    public async Task<IActionResult> Cancel(string orderNumber, CancellationToken ct) =>
        Done(await orders.AdminCancelAsync(orderNumber, ct), orderNumber);

    private RedirectToActionResult Done(OrderResult result, string orderNumber)
    {
        Flash(result.Success, result.Message);
        return RedirectToAction(nameof(Details), new { orderNumber });
    }
}

using GameShot.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameShot.Web.Services.Mail;

public enum OrderMailKind
{
    Paid,
    Shipped,
    Cancelled,
}

/// <summary>주문 상태가 바뀔 때 고객에게 보낼 메일을 아웃박스에 넣는다</summary>
public class OrderMailer(ApplicationDbContext db, MailQueue queue, IOptions<MailOptions> options)
{
    /// <param name="save">false면 호출한 쪽 트랜잭션의 SaveChanges에 맡긴다</param>
    public async Task QueueAsync(int orderId, OrderMailKind kind, bool save = true, CancellationToken ct = default)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Items.OrderBy(i => i.Id))
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order?.User.Email is not { } email)
        {
            return;
        }

        var link = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/orders/{Uri.EscapeDataString(order.OrderNumber)}";
        var (mailKind, content) = kind switch
        {
            OrderMailKind.Paid => (MailKinds.OrderPaid, MailTemplates.OrderPaid(order, link)),
            OrderMailKind.Shipped => (MailKinds.OrderShipped, MailTemplates.OrderShipped(order, link)),
            _ => (MailKinds.OrderCancelled, MailTemplates.OrderCancelled(order, link)),
        };
        await queue.EnqueueAsync(email, mailKind, content, save, ct);
    }
}

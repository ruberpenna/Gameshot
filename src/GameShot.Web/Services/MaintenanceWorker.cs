using Microsoft.Extensions.Options;

namespace GameShot.Web.Services;

/// <summary>
/// 주기적으로 도는 정리 작업: 결제 기한이 지난 주문 취소(재고 복구), 오래된 비회원 장바구니 삭제.
/// 각 작업은 조건부 UPDATE/DELETE라 여러 번 돌거나 서버가 여러 대여도 안전하다.
/// </summary>
public class MaintenanceWorker(
    IServiceScopeFactory scopes,
    IOptions<ShopOptions> options,
    TimeProvider clock,
    ILogger<MaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.MaintenanceInterval, clock);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
            var carts = scope.ServiceProvider.GetRequiredService<CartService>();

            var expired = await orders.ExpirePendingOrdersAsync(ct);
            if (expired > 0)
            {
                logger.LogInformation("결제 시간이 지난 주문 {Count}건을 취소하고 재고를 되돌렸습니다.", expired);
            }

            var removed = await carts.DeleteStaleGuestCartsAsync(options.Value.GuestCartRetention, ct);
            if (removed > 0)
            {
                logger.LogInformation("오래된 비회원 장바구니 {Count}개를 지웠습니다.", removed);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 종료 중
        }
        catch (Exception ex)
        {
            // DB가 잠깐 끊겨도 다음 주기에 다시 시도한다
            logger.LogError(ex, "정리 작업이 실패했습니다.");
        }
    }
}

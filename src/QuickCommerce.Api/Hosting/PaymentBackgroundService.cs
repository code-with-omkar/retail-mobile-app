using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Api.Hosting;

/// <summary>
/// Runs the payment upkeep every minute or so while online payment is on: releases the items of orders not paid in time, asks the provider for the
/// refunds that are due, and looks up payments whose notification never arrived. Each step is safe to run twice or on several servers at once,
/// and a failure in one never stops the others or the next round.
/// </summary>
public sealed class PaymentBackgroundService(IServiceScopeFactory scopes, PaymentSettings settings, ILogger<PaymentBackgroundService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.JobIntervalSeconds));
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var work = scope.ServiceProvider.GetRequiredService<IPaymentMaintenance>();
        await Step("release held items", () => work.ReleaseExpiredHoldsAsync(cancellationToken));
        await Step("start refunds", () => work.StartRefundsAsync(cancellationToken));
        await Step("look up unsettled payments", () => work.ReconcileAsync(cancellationToken));
    }

    private async Task Step(string name, Func<Task<int>> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Payment upkeep step failed: {Step}", name);
        }
    }
}

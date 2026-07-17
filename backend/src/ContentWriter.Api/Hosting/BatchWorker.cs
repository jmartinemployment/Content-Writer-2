using ContentWriter.Application.Services.Batch;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentWriter.Api.Hosting;

/// <summary>
/// DB-backed queue worker: polls for Queued BatchJobItems and runs them detached from HTTP.
/// SemaphoreSlim caps concurrent project pipelines (config, default 2) — separate from the
/// global LLM-call cap (LlmConcurrencyGate), since review passes stack additional calls on top
/// of generation and a per-project cap alone would undercount that.
/// </summary>
public sealed class BatchWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BatchWorker> _logger;
    private readonly SemaphoreSlim _projectConcurrency;

    public BatchWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<BatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var maxConcurrentProjects = configuration.GetValue<int?>("Batch:MaxConcurrentProjects") ?? 2;
        _projectConcurrency = new SemaphoreSlim(maxConcurrentProjects, maxConcurrentProjects);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchWorker poll iteration failed");
            }

            await Task.Delay(PollInterval, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private async Task PollOnceAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentWriterDbContext>();

        var queuedItemIds = await db.BatchJobItems
            .Where(i => i.Status == BatchJobItemStatus.Queued)
            .OrderBy(i => i.CreatedAtUtc)
            .Select(i => i.Id)
            .Take(50)
            .ToListAsync(stoppingToken);

        if (queuedItemIds.Count == 0)
            return;

        // Flip any Queued job to Running so progress endpoints reflect activity immediately.
        var jobIds = await db.BatchJobItems
            .Where(i => queuedItemIds.Contains(i.Id))
            .Select(i => i.BatchJobId)
            .Distinct()
            .ToListAsync(stoppingToken);

        await db.BatchJobs
            .Where(j => jobIds.Contains(j.Id) && j.Status == BatchJobStatus.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.Status, BatchJobStatus.Running)
                .SetProperty(j => j.StartedAtUtc, DateTime.UtcNow), stoppingToken);

        var tasks = queuedItemIds.Select(itemId => RunGatedAsync(itemId, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task RunGatedAsync(Guid batchJobItemId, CancellationToken stoppingToken)
    {
        await _projectConcurrency.WaitAsync(stoppingToken);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<IBatchPipelineRunner>();
            await runner.RunItemAsync(batchJobItemId, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchJobItem {ItemId} runner threw unexpectedly", batchJobItemId);
        }
        finally
        {
            _projectConcurrency.Release();
        }
    }
}

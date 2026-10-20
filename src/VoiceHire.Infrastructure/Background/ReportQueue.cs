using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VoiceHire.Application.Abstractions;
using VoiceHire.Application.Reports;
using VoiceHire.Domain.Enums;
using VoiceHire.Infrastructure.Persistence;

namespace VoiceHire.Infrastructure.Background;

public sealed class ReportQueue : IReportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(sessionId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Generates reports off the request path. On startup it re-queues interviews that finished but
/// were not evaluated (for example after a restart). Move to a separate Worker Service when needed.
/// </summary>
public sealed class ReportWorker(ReportQueue queue, IServiceScopeFactory scopeFactory, ILogger<ReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeuePendingAsync(stoppingToken);

        await foreach (var sessionId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReportGenerator>().GenerateAsync(sessionId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not generate report for interview {SessionId}", sessionId);
            }
        }
    }

    private async Task RequeuePendingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoiceHireDbContext>();
        var pending = await db.InterviewSessions
            .Where(s => s.Status == InterviewStatus.Completed || s.Status == InterviewStatus.Evaluating)
            .Select(s => s.Id)
            .ToListAsync(ct);
        foreach (var id in pending) await queue.EnqueueAsync(id, ct);
    }
}

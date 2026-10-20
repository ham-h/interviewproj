using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VoiceHire.Application.Abstractions;
using VoiceHire.Application.Interviews;
using VoiceHire.Domain.Enums;
using VoiceHire.Infrastructure.Persistence;

namespace VoiceHire.Infrastructure.AI;

/// <summary>
/// Opens a server-side ("sideband") WebSocket to each live call. This is where the transcript is
/// persisted and tool calls are answered, so neither depends on anything the browser reports.
/// </summary>
public sealed class RealtimeCallMonitor(
    IServiceScopeFactory scopeFactory,
    IOptions<OpenAiOptions> options,
    TimeProvider clock,
    ILogger<RealtimeCallMonitor> logger) : IRealtimeCallMonitor, IHostedService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _calls = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> HandledEvents =
    [
        "input_audio_buffer.committed",
        "conversation.item.input_audio_transcription.completed",
        "response.output_audio_transcript.done",
        "response.function_call_arguments.done",
        "error"
    ];

    public void Attach(Guid sessionId, string providerCallId)
    {
        Detach(sessionId); // a reconnect replaces the previous call
        var cts = new CancellationTokenSource();
        _calls[sessionId] = cts;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(sessionId, providerCallId, cts.Token); }
            finally
            {
                _calls.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(sessionId, cts));
                cts.Dispose();
            }
        });
    }

    public void Detach(Guid sessionId)
    {
        if (_calls.TryRemove(sessionId, out var cts))
            cts.Cancel();
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var id in _calls.Keys) Detach(id);
        return Task.CompletedTask;
    }

    private async Task RunAsync(Guid sessionId, string callId, CancellationToken ct)
    {
        using var logScope = logger.BeginScope(new Dictionary<string, object> { ["InterviewId"] = sessionId, ["CallId"] = callId });
        using var ws = new ClientWebSocket();
        var sendLock = new SemaphoreSlim(1, 1);
        // Maps a committed candidate audio item to the question that was open when they spoke,
        // because its transcription can arrive after the interviewer has moved on.
        var itemTurns = new Dictionary<string, int?>();

        try
        {
            ws.Options.SetRequestHeader("Authorization", $"Bearer {options.Value.ApiKey}");
            await ws.ConnectAsync(new Uri($"{options.Value.RealtimeWebSocketUrl}?call_id={Uri.EscapeDataString(callId)}"), ct);
            logger.LogInformation("Call monitor attached");

            _ = EnforceTimeLimitAsync(sessionId, ws, sendLock, ct);

            await foreach (var evt in ReceiveEventsAsync(ws, ct))
            {
                using (evt)
                {
                    await HandleEventAsync(sessionId, evt.RootElement, ws, sendLock, itemTurns, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Call monitor failed");
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation("Call monitor detached");
        }
    }

    private async Task HandleEventAsync(Guid sessionId, JsonElement evt, ClientWebSocket ws, SemaphoreSlim sendLock, Dictionary<string, int?> itemTurns, CancellationToken ct)
    {
        var type = evt.GetProperty("type").GetString();
        if (type is null || !HandledEvents.Contains(type)) return; // audio deltas etc. are high-volume and irrelevant here

        using var scope = scopeFactory.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<InterviewOrchestrator>();

        switch (type)
        {
            case "input_audio_buffer.committed":
                itemTurns[evt.GetProperty("item_id").GetString()!] = await orchestrator.CurrentTurnNumberAsync(sessionId, ct);
                break;

            case "conversation.item.input_audio_transcription.completed":
            {
                var itemId = evt.GetProperty("item_id").GetString()!;
                itemTurns.Remove(itemId, out var turnNo);
                await orchestrator.RecordUtteranceAsync(sessionId, turnNo, Speaker.Candidate, evt.GetProperty("transcript").GetString() ?? "", ct);
                break;
            }

            case "response.output_audio_transcript.done":
                await orchestrator.RecordUtteranceAsync(sessionId, null, Speaker.Interviewer, evt.GetProperty("transcript").GetString() ?? "", ct);
                break;

            case "response.function_call_arguments.done":
            {
                var output = await HandleToolCallAsync(sessionId, orchestrator,
                    evt.GetProperty("name").GetString()!, evt.GetProperty("arguments").GetString() ?? "{}", ct);
                await SendAsync(ws, sendLock, new
                {
                    type = "conversation.item.create",
                    item = new { type = "function_call_output", call_id = evt.GetProperty("call_id").GetString(), output = JsonSerializer.Serialize(output, Json) }
                }, ct);
                await SendAsync(ws, sendLock, new { type = "response.create" }, ct);
                break;
            }

            case "error":
                logger.LogWarning("Realtime error event: {Error}", evt.GetProperty("error").ToString());
                break;
        }
    }

    private async Task<object> HandleToolCallAsync(Guid sessionId, InterviewOrchestrator orchestrator, string name, string arguments, CancellationToken ct)
    {
        using var args = JsonDocument.Parse(arguments);
        switch (name)
        {
            case InterviewTools.GetNextQuestion:
                var signal = args.RootElement.TryGetProperty("previous_answer", out var p) ? ParseSignal(p.GetString()) : null;
                var next = await orchestrator.GetNextQuestionAsync(sessionId, signal, ct);
                return next.InterviewOver
                    ? new { interview_over = true, reason = next.Reason }
                    : new { interview_over = false, question = next.Question, category = next.Category, question_number = next.Number, total_questions = next.Max };

            case InterviewTools.EndInterview:
                await orchestrator.FinishAsync(sessionId, ct);
                return new { ended = true, instruction = "Thank the candidate and say goodbye in one or two sentences. Do not mention scores." };

            default:
                logger.LogWarning("Unknown tool {Tool}", name);
                return new { error = $"Unknown tool '{name}'." };
        }
    }

    private static AnswerSignal? ParseSignal(string? value) => value switch
    {
        "strong" => AnswerSignal.Strong,
        "partial" => AnswerSignal.Partial,
        "weak" => AnswerSignal.Weak,
        "no_answer" => AnswerSignal.NoAnswer,
        _ => null
    };

    /// <summary>The time limit is enforced here, not by the prompt: ask for a closing message, then end the interview.</summary>
    private async Task EnforceTimeLimitAsync(Guid sessionId, ClientWebSocket ws, SemaphoreSlim sendLock, CancellationToken ct)
    {
        try
        {
            DateTimeOffset deadline;
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VoiceHireDbContext>();
                var s = await db.InterviewSessions.AsNoTracking()
                    .Where(x => x.Id == sessionId)
                    .Select(x => new { x.StartedAt, x.Template.MaxDurationMinutes })
                    .SingleAsync(ct);
                deadline = (s.StartedAt ?? clock.GetUtcNow()).AddMinutes(s.MaxDurationMinutes);
            }

            var remaining = deadline - clock.GetUtcNow();
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, clock, ct);

            await SendAsync(ws, sendLock, new
            {
                type = "conversation.item.create",
                item = new { type = "message", role = "system", content = new[] { new { type = "input_text", text = "The time limit has been reached. Call end_interview now and close politely." } } }
            }, ct);
            await SendAsync(ws, sendLock, new { type = "response.create" }, ct);

            await Task.Delay(TimeSpan.FromSeconds(options.Value.TimeLimitGraceSeconds), clock, ct);
            using var finishScope = scopeFactory.CreateScope();
            await finishScope.ServiceProvider.GetRequiredService<InterviewOrchestrator>().FinishAsync(sessionId, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Time-limit enforcement failed");
        }
    }

    private static async Task SendAsync(ClientWebSocket ws, SemaphoreSlim sendLock, object payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        await sendLock.WaitAsync(ct);
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
        finally { sendLock.Release(); }
    }

    private static async IAsyncEnumerable<JsonDocument> ReceiveEventsAsync(ClientWebSocket ws, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) yield break;

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            message.Position = 0;
            var doc = await JsonDocument.ParseAsync(message, cancellationToken: ct);
            message.SetLength(0);
            yield return doc;
        }
    }
}

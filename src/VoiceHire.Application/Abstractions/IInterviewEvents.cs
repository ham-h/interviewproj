namespace VoiceHire.Application.Abstractions;

/// <summary>
/// In-process notifications for interactive Blazor components (which already run over SignalR).
/// Swap for a SignalR hub or a message bus if the API and UI are ever hosted separately.
/// </summary>
public interface IInterviewEvents
{
    event Action<QuestionAdvanced>? QuestionAdvancedRaised;
    event Action<Guid>? SessionEnded;
    event Action<Guid>? ReportReady;

    void RaiseQuestionAdvanced(QuestionAdvanced e);
    void RaiseSessionEnded(Guid sessionId);
    void RaiseReportReady(Guid sessionId);
}

public sealed record QuestionAdvanced(Guid SessionId, int Number, int Max, string Category);

public interface IReportQueue
{
    ValueTask EnqueueAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

using VoiceHire.Application.Abstractions;

namespace VoiceHire.Infrastructure.Background;

public sealed class InterviewEvents : IInterviewEvents
{
    public event Action<QuestionAdvanced>? QuestionAdvancedRaised;
    public event Action<Guid>? SessionEnded;
    public event Action<Guid>? ReportReady;

    public void RaiseQuestionAdvanced(QuestionAdvanced e) => QuestionAdvancedRaised?.Invoke(e);
    public void RaiseSessionEnded(Guid sessionId) => SessionEnded?.Invoke(sessionId);
    public void RaiseReportReady(Guid sessionId) => ReportReady?.Invoke(sessionId);
}

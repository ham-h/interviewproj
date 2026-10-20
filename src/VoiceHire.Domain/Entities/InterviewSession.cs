using VoiceHire.Domain.Enums;

namespace VoiceHire.Domain.Entities;

public class InterviewSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public required string CandidateId { get; set; }
    public Guid TemplateId { get; set; }
    public InterviewTemplate Template { get; set; } = null!;
    public Guid RubricId { get; set; }
    public ScoringRubric Rubric { get; set; } = null!;

    public InterviewStatus Status { get; private set; } = InterviewStatus.Created;
    public Difficulty CurrentDifficulty { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>The realtime provider's call id. Kept separate from our own session id.</summary>
    public string? ProviderCallId { get; private set; }
    public int ConnectionCount { get; private set; }

    public List<InterviewTurn> Turns { get; set; } = [];
    public InterviewReport? Report { get; set; }

    public bool IsLive => Status is InterviewStatus.Created or InterviewStatus.InProgress;

    public InterviewTurn? CurrentTurn => Turns.OrderByDescending(t => t.SequenceNo).FirstOrDefault();

    public void AttachRealtimeCall(string providerCallId, DateTimeOffset now)
    {
        if (!IsLive)
            throw new InvalidOperationException($"Cannot connect to an interview in status {Status}.");

        ProviderCallId = providerCallId;
        ConnectionCount++;
        StartedAt ??= now;
        Status = InterviewStatus.InProgress;
    }

    public bool HasExceededDuration(DateTimeOffset now, int maxDurationMinutes) =>
        StartedAt is { } started && now - started >= TimeSpan.FromMinutes(maxDurationMinutes);

    public void Complete(DateTimeOffset now)
    {
        if (!IsLive) return;
        // An interview that never got an answer is abandoned rather than evaluated.
        Status = Turns.Any(t => t.HasCandidateAnswer) ? InterviewStatus.Completed : InterviewStatus.Abandoned;
        EndedAt = now;
    }

    public void MarkEvaluating() => Status = InterviewStatus.Evaluating;
    public void MarkEvaluated() => Status = InterviewStatus.Evaluated;
    public void MarkEvaluationFailed() => Status = InterviewStatus.EvaluationFailed;
}

public class InterviewTurn
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public int SequenceNo { get; set; }
    public Guid? QuestionId { get; set; }
    public required string QuestionText { get; set; }
    public required string Category { get; set; }
    public Difficulty Difficulty { get; set; }

    /// <summary>The live interviewer's read of the answer, used only to adapt difficulty.</summary>
    public AnswerSignal? InterviewerSignal { get; set; }
    public DateTimeOffset AskedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AnsweredAt { get; set; }

    /// <summary>Everything said while this question was open, including follow-ups and clarifications.</summary>
    public List<Utterance> Utterances { get; set; } = [];
    public TurnAssessment? Assessment { get; set; }

    public bool HasCandidateAnswer => Utterances.Any(u => u.Speaker == Speaker.Candidate);

    public void AddUtterance(Speaker speaker, string text, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Utterances.Add(new Utterance { Speaker = speaker, Text = text.Trim(), At = at });
        if (speaker == Speaker.Candidate) AnsweredAt = at;
    }
}

public class Utterance
{
    public Speaker Speaker { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset At { get; set; }
}

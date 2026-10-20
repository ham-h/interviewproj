namespace VoiceHire.Domain.Entities;

public class TurnAssessment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TurnId { get; set; }
    public List<CriterionScore> Scores { get; set; } = [];
    public List<EvidenceItem> Evidence { get; set; } = [];
    public List<string> MissingConcepts { get; set; } = [];
    public required string Feedback { get; set; }
    public bool NeedsHumanReview { get; set; }

    /// <summary>0–100, calculated in C# from <see cref="Scores"/>; never taken from the model.</summary>
    public decimal WeightedScore { get; set; }
    public required string ModelVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class CriterionScore
{
    public required string Criterion { get; set; }
    public int Score { get; set; }
}

public class EvidenceItem
{
    public required string Criterion { get; set; }
    public required string Observation { get; set; }

    /// <summary>Verbatim excerpt from the candidate's transcript; checked against the transcript before saving.</summary>
    public required string Quote { get; set; }
}

public class InterviewReport
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public decimal TotalScore { get; set; }
    public int RubricVersion { get; set; }
    public required string Summary { get; set; }
    public List<string> Strengths { get; set; } = [];
    public List<string> Gaps { get; set; } = [];
    public bool NeedsHumanReview { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public decimal? AdjustedScore { get; set; }
    public string? ReviewerNotes { get; set; }

    public decimal EffectiveScore => AdjustedScore ?? TotalScore;
}

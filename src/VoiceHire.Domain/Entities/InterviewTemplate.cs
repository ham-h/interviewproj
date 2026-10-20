using VoiceHire.Domain.Enums;

namespace VoiceHire.Domain.Entities;

public class InterviewTemplate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public required string Title { get; set; }
    public required string Role { get; set; }
    public string? Description { get; set; }
    public Difficulty StartingDifficulty { get; set; } = Difficulty.Basic;
    public int MaxQuestions { get; set; } = 7;
    public int MaxDurationMinutes { get; set; } = 20;
    public bool IsPublished { get; set; }

    public List<Question> Questions { get; set; } = [];
    public List<ScoringRubric> Rubrics { get; set; } = [];
}

public class Question
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TemplateId { get; set; }
    public required string Category { get; set; }
    public required string Text { get; set; }
    public Difficulty Difficulty { get; set; }

    /// <summary>Concepts a complete answer should cover. Given to the evaluator, never to the live interviewer.</summary>
    public List<string> ExpectedConcepts { get; set; } = [];
    public bool IsActive { get; set; } = true;

    public QuestionSource Source { get; set; } = QuestionSource.Manual;

    /// <summary>File name of the uploaded document an imported question came from. The document itself is not stored.</summary>
    public string? SourceDocument { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Versioned so historical reports stay interpretable after the criteria change.</summary>
public class ScoringRubric
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TemplateId { get; set; }
    public int Version { get; set; }
    public List<RubricCriterion> Criteria { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RubricCriterion
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public int Weight { get; set; }
}

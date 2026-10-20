namespace VoiceHire.Domain.Enums;

public enum InterviewStatus
{
    Created = 0,
    InProgress = 1,
    Completed = 2,
    Evaluating = 3,
    Evaluated = 4,
    Abandoned = 5,
    EvaluationFailed = 6
}

public enum Difficulty
{
    Basic = 1,
    Intermediate = 2,
    Advanced = 3
}

public enum OrganizationRole
{
    Member = 0,
    Reviewer = 1,
    Admin = 2
}

public enum QuestionSource
{
    Seeded = 0,
    Manual = 1,
    Imported = 2
}

public enum Speaker
{
    Interviewer = 0,
    Candidate = 1
}

/// <summary>
/// The live interviewer's rough read of the previous answer. It only steers difficulty;
/// it is never used as a score. Scoring is done separately against the rubric.
/// </summary>
public enum AnswerSignal
{
    Strong,
    Partial,
    Weak,
    NoAnswer
}

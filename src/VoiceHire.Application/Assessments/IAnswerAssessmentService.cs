using VoiceHire.Domain.Entities;

namespace VoiceHire.Application.Assessments;

/// <summary>
/// Scores one question's exchange against the rubric. Deliberately separate from the live
/// interviewer model, so it can use a different (or more consistent) model and be re-run.
/// </summary>
public interface IAnswerAssessmentService
{
    Task<AnswerAssessmentResult> AssessAsync(AnswerAssessmentRequest request, CancellationToken cancellationToken);
}

public sealed record AnswerAssessmentRequest(
    string Role,
    string QuestionText,
    string Category,
    IReadOnlyList<string> ExpectedConcepts,
    IReadOnlyList<Utterance> Exchange,
    IReadOnlyList<RubricCriterion> Criteria);

public sealed record AnswerAssessmentResult(
    IReadOnlyDictionary<string, int> Scores,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<string> MissingConcepts,
    string Feedback,
    bool NeedsHumanReview,
    string ModelVersion);

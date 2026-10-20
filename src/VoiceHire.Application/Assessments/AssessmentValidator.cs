using System.Text.RegularExpressions;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Enums;
using VoiceHire.Domain.Rules;

namespace VoiceHire.Application.Assessments;

public static partial class AssessmentValidator
{
    /// <summary>
    /// Rejects malformed scores and drops evidence whose quote does not appear in what the candidate
    /// actually said. Dropped evidence flags the answer for human review rather than failing it.
    /// </summary>
    public static AnswerAssessmentResult Validate(AnswerAssessmentResult result, IReadOnlyList<RubricCriterion> criteria, IReadOnlyList<Utterance> exchange)
    {
        foreach (var criterion in criteria)
        {
            if (!result.Scores.TryGetValue(criterion.Key, out var score))
                throw new AssessmentValidationException($"Missing score for '{criterion.Key}'.");
            if (score is < ScoreCalculator.MinCriterionScore or > ScoreCalculator.MaxCriterionScore)
                throw new AssessmentValidationException($"Score {score} for '{criterion.Key}' is out of range.");
        }

        var candidateText = Normalize(string.Join(' ', exchange.Where(u => u.Speaker == Speaker.Candidate).Select(u => u.Text)));
        var keys = criteria.Select(c => c.Key).ToHashSet();

        var supported = result.Evidence
            .Where(e => keys.Contains(e.Criterion) && !string.IsNullOrWhiteSpace(e.Quote) && candidateText.Contains(Normalize(e.Quote)))
            .ToList();

        var unsupported = supported.Count < result.Evidence.Count;
        return result with
        {
            Evidence = supported,
            NeedsHumanReview = result.NeedsHumanReview || unsupported
        };
    }

    private static string Normalize(string text) =>
        Whitespace().Replace(Punctuation().Replace(text.ToLowerInvariant(), " "), " ").Trim();

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

public class AssessmentValidationException(string message) : Exception(message);

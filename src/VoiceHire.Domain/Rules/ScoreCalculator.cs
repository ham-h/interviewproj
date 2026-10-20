using VoiceHire.Domain.Entities;

namespace VoiceHire.Domain.Rules;

public static class ScoreCalculator
{
    public const int MinCriterionScore = 1;
    public const int MaxCriterionScore = 5;

    /// <summary>
    /// Weighted 0–100 score. Each 1–5 criterion score is normalised to 0–1 first,
    /// so an all-1 answer scores 0 and an all-5 answer scores 100.
    /// </summary>
    public static decimal WeightedScore(IReadOnlyCollection<RubricCriterion> criteria, IReadOnlyDictionary<string, int> scores)
    {
        var totalWeight = criteria.Sum(c => c.Weight);
        if (totalWeight <= 0)
            throw new ArgumentException("Rubric weights must sum to a positive number.", nameof(criteria));

        decimal weighted = 0;
        foreach (var criterion in criteria)
        {
            if (!scores.TryGetValue(criterion.Key, out var score))
                throw new ArgumentException($"Missing score for criterion '{criterion.Key}'.", nameof(scores));
            if (score is < MinCriterionScore or > MaxCriterionScore)
                throw new ArgumentOutOfRangeException(nameof(scores), score, $"Score for '{criterion.Key}' must be {MinCriterionScore}–{MaxCriterionScore}.");

            weighted += criterion.Weight * (score - MinCriterionScore) / (decimal)(MaxCriterionScore - MinCriterionScore);
        }

        return Math.Round(weighted * 100 / totalWeight, 1);
    }
}

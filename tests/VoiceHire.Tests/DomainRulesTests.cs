using VoiceHire.Application.Assessments;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Enums;
using VoiceHire.Domain.Rules;

namespace VoiceHire.Tests;

public class ScoreCalculatorTests
{
    private static readonly RubricCriterion[] Criteria =
    [
        new() { Key = "t", Name = "T", Description = "", Weight = 40 },
        new() { Key = "r", Name = "R", Description = "", Weight = 25 },
        new() { Key = "c", Name = "C", Description = "", Weight = 20 },
        new() { Key = "l", Name = "L", Description = "", Weight = 15 }
    ];

    private static Dictionary<string, int> All(int score) => Criteria.ToDictionary(c => c.Key, _ => score);

    [Fact]
    public void All_ones_score_zero_and_all_fives_score_one_hundred()
    {
        Assert.Equal(0m, ScoreCalculator.WeightedScore(Criteria, All(1)));
        Assert.Equal(100m, ScoreCalculator.WeightedScore(Criteria, All(5)));
        Assert.Equal(50m, ScoreCalculator.WeightedScore(Criteria, All(3)));
    }

    [Fact]
    public void Weights_are_applied()
    {
        var scores = new Dictionary<string, int> { ["t"] = 5, ["r"] = 1, ["c"] = 1, ["l"] = 1 };
        Assert.Equal(40m, ScoreCalculator.WeightedScore(Criteria, scores));
    }

    [Fact]
    public void Missing_or_out_of_range_scores_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ScoreCalculator.WeightedScore(Criteria, new Dictionary<string, int> { ["t"] = 3 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScoreCalculator.WeightedScore(Criteria, All(6)));
    }
}

public class DifficultyPolicyTests
{
    [Theory]
    [InlineData(Difficulty.Basic, AnswerSignal.Strong, Difficulty.Intermediate)]
    [InlineData(Difficulty.Advanced, AnswerSignal.Strong, Difficulty.Advanced)]
    [InlineData(Difficulty.Intermediate, AnswerSignal.Weak, Difficulty.Basic)]
    [InlineData(Difficulty.Basic, AnswerSignal.NoAnswer, Difficulty.Basic)]
    [InlineData(Difficulty.Intermediate, AnswerSignal.Partial, Difficulty.Intermediate)]
    [InlineData(Difficulty.Intermediate, null, Difficulty.Intermediate)]
    public void Adjusts_within_bounds(Difficulty current, AnswerSignal? signal, Difficulty expected) =>
        Assert.Equal(expected, DifficultyPolicy.Next(current, signal));
}

public class AssessmentValidatorTests
{
    private static readonly RubricCriterion[] Criteria = [new() { Key = "t", Name = "T", Description = "", Weight = 1 }];

    private static readonly Utterance[] Exchange =
    [
        new() { Speaker = Speaker.Interviewer, Text = "What is dependency injection?" },
        new() { Speaker = Speaker.Candidate, Text = "You register services, and the container injects them through the constructor." }
    ];

    private static AnswerAssessmentResult Result(int score, params EvidenceItem[] evidence) =>
        new(new Dictionary<string, int> { ["t"] = score }, evidence, [], "ok", false, "test");

    [Fact]
    public void Keeps_evidence_quoted_from_the_candidate_ignoring_case_and_punctuation()
    {
        var result = AssessmentValidator.Validate(
            Result(4, new EvidenceItem { Criterion = "t", Observation = "Mentions constructor injection", Quote = "Injects them through the constructor" }),
            Criteria, Exchange);

        Assert.Single(result.Evidence);
        Assert.False(result.NeedsHumanReview);
    }

    [Fact]
    public void Drops_unsupported_evidence_and_flags_for_review()
    {
        // The quote is from the interviewer, not the candidate.
        var result = AssessmentValidator.Validate(
            Result(4, new EvidenceItem { Criterion = "t", Observation = "Defines DI", Quote = "What is dependency injection" }),
            Criteria, Exchange);

        Assert.Empty(result.Evidence);
        Assert.True(result.NeedsHumanReview);
    }

    [Fact]
    public void Rejects_out_of_range_scores() =>
        Assert.Throws<AssessmentValidationException>(() => AssessmentValidator.Validate(Result(0), Criteria, Exchange));
}

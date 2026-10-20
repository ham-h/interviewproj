using VoiceHire.Domain.Entities;

namespace VoiceHire.Domain.Rules;

public static class DefaultRubric
{
    public static List<RubricCriterion> Criteria() =>
    [
        new() { Key = "technicalAccuracy", Name = "Technical accuracy", Description = "Concepts and facts are correct.", Weight = 40 },
        new() { Key = "reasoning", Name = "Reasoning", Description = "Explains why and how, weighs trade-offs, applies to scenarios.", Weight = 25 },
        new() { Key = "completeness", Name = "Completeness", Description = "Covers the key concepts the question calls for.", Weight = 20 },
        new() { Key = "clarity", Name = "Clarity", Description = "The explanation is relevant and easy to follow. Not about accent or fluency.", Weight = 15 }
    ];
}

using VoiceHire.Domain.Enums;

namespace VoiceHire.Domain.Rules;

public static class DifficultyPolicy
{
    public static Difficulty Next(Difficulty current, AnswerSignal? lastSignal) => lastSignal switch
    {
        AnswerSignal.Strong => (Difficulty)Math.Min((int)current + 1, (int)Difficulty.Advanced),
        AnswerSignal.Weak or AnswerSignal.NoAnswer => (Difficulty)Math.Max((int)current - 1, (int)Difficulty.Basic),
        _ => current
    };
}

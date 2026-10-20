namespace VoiceHire.Infrastructure.AI;

public class OpenAiOptions
{
    public const string Section = "OpenAI";

    /// <summary>Server-side only. Set with user-secrets or an environment variable, never in appsettings.json.</summary>
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string RealtimeWebSocketUrl { get; set; } = "wss://api.openai.com/v1/realtime";

    public string RealtimeModel { get; set; } = "gpt-realtime-2.1";
    public string Voice { get; set; } = "marin";
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";
    public string TranscriptionLanguage { get; set; } = "en";

    /// <summary>Model for the separate rubric-based evaluator. Check the provider's current model list.</summary>
    public string AssessmentModel { get; set; } = "gpt-5.1";

    /// <summary>Model that finds questions in uploaded documents.</summary>
    public string QuestionExtractionModel { get; set; } = "gpt-5.1";

    /// <summary>Grace period for the interviewer's closing message after the time limit, before the call is ended.</summary>
    public int TimeLimitGraceSeconds { get; set; } = 45;
}

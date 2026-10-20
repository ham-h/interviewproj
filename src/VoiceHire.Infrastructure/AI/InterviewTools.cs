namespace VoiceHire.Infrastructure.AI;

/// <summary>Function tools the realtime interviewer can call. They are answered server-side by the call monitor.</summary>
internal static class InterviewTools
{
    public const string GetNextQuestion = "get_next_question";
    public const string EndInterview = "end_interview";

    public static object[] Definitions =>
    [
        new
        {
            type = "function",
            name = GetNextQuestion,
            description = "Get the next main interview question. Call this to start the interview and whenever the current topic is finished.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    previous_answer = new
                    {
                        type = "string",
                        @enum = new[] { "none", "strong", "partial", "weak", "no_answer" },
                        description = "Your honest read of the candidate's answer to the previous main question, or 'none' for the first question."
                    }
                },
                required = new[] { "previous_answer" },
                additionalProperties = false
            }
        },
        new
        {
            type = "function",
            name = EndInterview,
            description = "End the interview. Call when get_next_question reports the interview is over, or when the candidate asks to stop.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    reason = new { type = "string", description = "Short reason the interview is ending." }
                },
                required = new[] { "reason" },
                additionalProperties = false
            }
        }
    ];
}

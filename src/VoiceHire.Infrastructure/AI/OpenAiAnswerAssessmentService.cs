using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VoiceHire.Application.Assessments;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Enums;

namespace VoiceHire.Infrastructure.AI;

/// <summary>
/// Rubric-based evaluator using the Responses API with a strict JSON schema. The model returns
/// per-criterion scores and evidence only; the weighted total is calculated in C#.
/// </summary>
public class OpenAiAnswerAssessmentService(HttpClient http, IOptions<OpenAiOptions> options) : IAnswerAssessmentService
{
    public const string PromptVersion = "assessor-v1";

    private const string Instructions = """
        You are a strict, fair technical assessor scoring one question from a spoken technical interview.
        Score each rubric criterion from 1 (very poor) to 5 (excellent) using only what the candidate said.

        Rules:
        - The transcript comes from speech recognition and may contain recognition errors, especially for
          technical terms (e.g. "eye queryable" for IQueryable). Interpret charitably; never penalise
          wording that is plausibly a transcription error.
        - Never score accent, fluency, grammar, confidence or personality.
        - Clarifying questions asked by the candidate are not wrong answers.
        - Every evidence item must include a short verbatim quote of the candidate's own words.
        - List expected concepts the candidate did not cover in missingConcepts.
        - Feedback is addressed to the candidate: two or three constructive sentences.
        - Set needsHumanReview to true if the transcript looks garbled or you are not confident.
        """;

    public async Task<AnswerAssessmentResult> AssessAsync(AnswerAssessmentRequest request, CancellationToken cancellationToken)
    {
        var model = options.Value.AssessmentModel;
        using var doc = await OpenAiResponses.GetStructuredAsync(
            http, model, Instructions, BuildInput(request), "answer_assessment", BuildSchema(request.Criteria), cancellationToken);
        var root = doc.RootElement;

        var scores = request.Criteria.ToDictionary(c => c.Key, c => root.GetProperty("scores").GetProperty(c.Key).GetInt32());
        var evidence = root.GetProperty("evidence").EnumerateArray().Select(e => new EvidenceItem
        {
            Criterion = e.GetProperty("criterion").GetString()!,
            Observation = e.GetProperty("observation").GetString()!,
            Quote = e.GetProperty("quote").GetString()!
        }).ToList();

        return new AnswerAssessmentResult(
            scores,
            evidence,
            root.GetProperty("missingConcepts").EnumerateArray().Select(x => x.GetString()!).ToList(),
            root.GetProperty("feedback").GetString()!,
            root.GetProperty("needsHumanReview").GetBoolean(),
            $"{model}/{PromptVersion}");
    }

    private static string BuildInput(AnswerAssessmentRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Role: {r.Role}");
        sb.AppendLine($"Category: {r.Category}");
        sb.AppendLine($"Question: {r.QuestionText}");
        sb.AppendLine($"Expected concepts: {string.Join("; ", r.ExpectedConcepts)}");
        sb.AppendLine();
        sb.AppendLine("Rubric:");
        foreach (var c in r.Criteria) sb.AppendLine($"- {c.Key} ({c.Name}): {c.Description}");
        sb.AppendLine();
        sb.AppendLine("Transcript of this question (interviewer follow-ups included):");
        foreach (var u in r.Exchange)
            sb.AppendLine($"{(u.Speaker == Speaker.Candidate ? "CANDIDATE" : "INTERVIEWER")}: {u.Text}");
        return sb.ToString();
    }

    private static object BuildSchema(IReadOnlyList<RubricCriterion> criteria)
    {
        var keys = criteria.Select(c => c.Key).ToArray();
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[] { "scores", "evidence", "missingConcepts", "feedback", "needsHumanReview" },
            ["properties"] = new Dictionary<string, object>
            {
                ["scores"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = keys,
                    ["properties"] = keys.ToDictionary(k => k, _ => (object)new { type = "integer", @enum = new[] { 1, 2, 3, 4, 5 } })
                },
                ["evidence"] = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "criterion", "observation", "quote" },
                        properties = new
                        {
                            criterion = new { type = "string", @enum = keys },
                            observation = new { type = "string" },
                            quote = new { type = "string" }
                        }
                    }
                },
                ["missingConcepts"] = new { type = "array", items = new { type = "string" } },
                ["feedback"] = new { type = "string" },
                ["needsHumanReview"] = new { type = "boolean" }
            }
        };
    }
}

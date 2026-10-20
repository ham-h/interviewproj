using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoiceHire.Application.Abstractions;
using VoiceHire.Application.Interviews;
using VoiceHire.Domain.Entities;
using VoiceHire.Infrastructure.AI;

namespace VoiceHire.Web.Endpoints;

public static class InterviewEndpoints
{
    public const string RealtimeRateLimitPolicy = "realtime";
    private const int MaxSdpLength = 64 * 1024;
    private const int MaxConnectionsPerInterview = 5;

    public static IEndpointRouteBuilder MapInterviewApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/interview-templates", async (ClaimsPrincipal user, IAppDbContext db, CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            var orgIds = db.OrganizationMembers.Where(m => m.UserId == userId).Select(m => m.OrganizationId);
            return await db.InterviewTemplates
                .Where(t => t.IsPublished && orgIds.Contains(t.OrganizationId))
                .Select(t => new TemplateDto(t.Id, t.Title, t.Role, t.Description, t.MaxQuestions, t.MaxDurationMinutes))
                .ToListAsync(ct);
        });

        api.MapPost("/interviews", async (CreateInterviewRequest body, ClaimsPrincipal user, InterviewOrchestrator orchestrator, CancellationToken ct) =>
        {
            try
            {
                var session = await orchestrator.CreateSessionAsync(user.GetUserId(), body.TemplateId, ct);
                return Results.Created($"/api/interviews/{session.Id}", ToDto(session));
            }
            catch (InterviewNotFoundException)
            {
                return Results.NotFound();
            }
        });

        api.MapGet("/interviews/{id:guid}", async (Guid id, ClaimsPrincipal user, InterviewOrchestrator orchestrator, InterviewAccess access, CancellationToken ct) =>
        {
            var session = await orchestrator.LoadAsync(id, ct);
            return session is not null && await access.CanViewAsync(user.GetUserId(), session, ct)
                ? Results.Ok(ToDto(session))
                : Results.NotFound();
        });

        api.MapGet("/interviews/{id:guid}/turns", async (Guid id, ClaimsPrincipal user, InterviewOrchestrator orchestrator, InterviewAccess access, CancellationToken ct) =>
        {
            var session = await orchestrator.LoadAsync(id, ct);
            if (session is null || !await access.CanViewAsync(user.GetUserId(), session, ct)) return Results.NotFound();
            return Results.Ok(session.Turns.OrderBy(t => t.SequenceNo).Select(t => new TurnDto(t.SequenceNo, t.Category, t.QuestionText, t.Utterances)));
        });

        // The browser posts its WebRTC SDP offer here; the provider key never leaves the server.
        // A cross-site page cannot send this: application/sdp is not a CORS-safelisted content type.
        api.MapPost("/interviews/{id:guid}/realtime-session", async (
            Guid id, HttpRequest request, ClaimsPrincipal user,
            InterviewOrchestrator orchestrator, IRealtimeInterviewProvider provider, IRealtimeCallMonitor monitor,
            TimeProvider clock, IOptions<OpenAiOptions> openAi, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            var session = await orchestrator.LoadAsync(id, ct);
            if (session is null || session.CandidateId != userId) return Results.NotFound();
            if (!session.IsLive || session.HasExceededDuration(clock.GetUtcNow(), session.Template.MaxDurationMinutes))
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This interview has ended.");
            if (session.ConnectionCount >= MaxConnectionsPerInterview)
                return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many reconnections for this interview.");
            if (request.ContentType?.StartsWith("application/sdp", StringComparison.OrdinalIgnoreCase) != true)
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

            using var reader = new StreamReader(request.Body);
            var sdp = await reader.ReadToEndAsync(ct);
            if (sdp.Length is 0 or > MaxSdpLength) return Results.BadRequest();

            if (string.IsNullOrWhiteSpace(openAi.Value.ApiKey))
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The voice service is not configured. Set OpenAI:ApiKey on the server.");

            RealtimeCallResult call;
            try
            {
                call = await provider.CreateCallAsync(
                    new RealtimeCallRequest(session.Id, sdp, InterviewerInstructions.Build(session), SafetyIdentifier(userId)), ct);
            }
            catch (Exception ex) when (ex is RealtimeProviderException or HttpRequestException)
            {
                loggers.CreateLogger("VoiceHire.Realtime").LogError(ex, "Could not create realtime call for interview {SessionId}", id);
                return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The voice service is unavailable. Please try again shortly.");
            }
            await orchestrator.AttachRealtimeCallAsync(session, call.ProviderCallId, ct);
            monitor.Attach(session.Id, call.ProviderCallId);

            return Results.Text(call.SdpAnswer, "application/sdp");
        }).RequireRateLimiting(RealtimeRateLimitPolicy);

        api.MapPost("/interviews/{id:guid}/finish", async (Guid id, ClaimsPrincipal user, InterviewOrchestrator orchestrator, IRealtimeCallMonitor monitor, CancellationToken ct) =>
        {
            var session = await orchestrator.LoadAsync(id, ct);
            if (session is null || session.CandidateId != user.GetUserId()) return Results.NotFound();
            await orchestrator.FinishAsync(id, ct); // idempotent
            monitor.Detach(id);
            return Results.Accepted($"/api/interviews/{id}/report");
        });

        api.MapGet("/interviews/{id:guid}/report", async (Guid id, ClaimsPrincipal user, InterviewOrchestrator orchestrator, InterviewAccess access, CancellationToken ct) =>
        {
            var session = await orchestrator.LoadAsync(id, ct);
            if (session is null || !await access.CanViewAsync(user.GetUserId(), session, ct)) return Results.NotFound();
            return session.Report is null
                ? Results.Accepted(value: new { status = session.Status.ToString() })
                : Results.Ok(session.Report);
        });

        api.MapPost("/interviews/{id:guid}/report/review", async (Guid id, ReviewRequest body, ClaimsPrincipal user, IAppDbContext db, InterviewAccess access, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            var session = await db.InterviewSessions.Include(s => s.Report).FirstOrDefaultAsync(s => s.Id == id, ct);
            if (session?.Report is null || !await access.IsReviewerAsync(userId, session.OrganizationId, ct)) return Results.NotFound();
            if (session.CandidateId == userId) return Results.Forbid();
            if (body.AdjustedScore is < 0 or > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["adjustedScore"] = ["Must be 0–100."] });

            session.Report.AdjustedScore = body.AdjustedScore;
            session.Report.ReviewerNotes = body.Notes;
            session.Report.ReviewedBy = userId;
            session.Report.ReviewedAt = clock.GetUtcNow();
            db.AuditEvents.Add(new AuditEvent { OrganizationId = session.OrganizationId, ActorUserId = userId, Action = "report.reviewed", EntityId = id.ToString() });
            await db.SaveChangesAsync(ct);
            return Results.Ok(session.Report);
        });

        return app;
    }

    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Authenticated user has no id.");

    /// <summary>A stable, non-reversible per-user id for the provider's abuse monitoring. No personal data is sent.</summary>
    private static string SafetyIdentifier(string userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("voicehire:" + userId)));

    private static InterviewDto ToDto(InterviewSession s) =>
        new(s.Id, s.TemplateId, s.Status.ToString(), s.CreatedAt, s.StartedAt, s.EndedAt, s.Turns.Count, s.Report?.EffectiveScore);
}

public sealed record CreateInterviewRequest(Guid TemplateId);
public sealed record ReviewRequest(decimal? AdjustedScore, string? Notes);
public sealed record TemplateDto(Guid Id, string Title, string Role, string? Description, int MaxQuestions, int MaxDurationMinutes);
public sealed record InterviewDto(Guid Id, Guid TemplateId, string Status, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, int QuestionsAsked, decimal? Score);
public sealed record TurnDto(int SequenceNo, string Category, string Question, IReadOnlyList<Utterance> Utterances);

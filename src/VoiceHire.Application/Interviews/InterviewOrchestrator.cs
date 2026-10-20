using Microsoft.EntityFrameworkCore;
using VoiceHire.Application.Abstractions;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Enums;
using VoiceHire.Domain.Rules;

namespace VoiceHire.Application.Interviews;

/// <summary>
/// The server-side authority for an interview. The realtime model asks it for questions through
/// tool calls; question choice, difficulty, question limits and duration limits are enforced here,
/// not in the prompt.
/// </summary>
public class InterviewOrchestrator(
    IAppDbContext db,
    InterviewAccess access,
    IReportQueue reportQueue,
    IInterviewEvents events,
    TimeProvider clock)
{
    public async Task<InterviewSession> CreateSessionAsync(string userId, Guid templateId, CancellationToken ct)
    {
        var template = await db.InterviewTemplates
            .Include(t => t.Rubrics)
            .FirstOrDefaultAsync(t => t.Id == templateId && t.IsPublished, ct)
            ?? throw new InterviewNotFoundException();

        if (!await access.IsMemberAsync(userId, template.OrganizationId, ct))
            throw new InterviewNotFoundException();

        var rubric = template.Rubrics.MaxBy(r => r.Version)
            ?? throw new InvalidOperationException($"Template {template.Id} has no rubric.");

        var session = new InterviewSession
        {
            OrganizationId = template.OrganizationId,
            CandidateId = userId,
            TemplateId = template.Id,
            RubricId = rubric.Id,
            CurrentDifficulty = template.StartingDifficulty,
            CreatedAt = clock.GetUtcNow()
        };
        db.InterviewSessions.Add(session);
        db.AuditEvents.Add(new AuditEvent { OrganizationId = template.OrganizationId, ActorUserId = userId, Action = "interview.created", EntityId = session.Id.ToString() });
        await db.SaveChangesAsync(ct);
        return session;
    }

    public Task<InterviewSession?> LoadAsync(Guid sessionId, CancellationToken ct) =>
        db.InterviewSessions
            .Include(s => s.Template)
            .Include(s => s.Rubric)
            .Include(s => s.Turns).ThenInclude(t => t.Assessment)
            .Include(s => s.Report)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public async Task AttachRealtimeCallAsync(InterviewSession session, string providerCallId, CancellationToken ct)
    {
        session.AttachRealtimeCall(providerCallId, clock.GetUtcNow());
        db.AuditEvents.Add(new AuditEvent { OrganizationId = session.OrganizationId, ActorUserId = session.CandidateId, Action = "interview.connected", EntityId = session.Id.ToString() });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Handles the model's get_next_question tool call.</summary>
    public async Task<NextQuestionResult> GetNextQuestionAsync(Guid sessionId, AnswerSignal? previousAnswer, CancellationToken ct)
    {
        var session = await LoadAsync(sessionId, ct) ?? throw new InterviewNotFoundException();
        var template = session.Template;
        var now = clock.GetUtcNow();

        if (!session.IsLive)
            return NextQuestionResult.Over("The interview has already ended.");

        if (session.CurrentTurn is { } previous)
        {
            previous.InterviewerSignal = previousAnswer;
            session.CurrentDifficulty = DifficultyPolicy.Next(session.CurrentDifficulty, previousAnswer);
        }

        if (session.Turns.Count >= template.MaxQuestions)
        {
            await db.SaveChangesAsync(ct);
            return NextQuestionResult.Over("All questions have been asked.");
        }
        if (session.HasExceededDuration(now, template.MaxDurationMinutes))
        {
            await db.SaveChangesAsync(ct);
            return NextQuestionResult.Over("The time limit has been reached.");
        }

        var question = await PickQuestionAsync(session, ct);
        if (question is null)
        {
            await db.SaveChangesAsync(ct);
            return NextQuestionResult.Over("The question bank for this interview is exhausted.");
        }

        var turn = new InterviewTurn
        {
            SessionId = session.Id,
            SequenceNo = session.Turns.Count + 1,
            QuestionId = question.Id,
            QuestionText = question.Text,
            Category = question.Category,
            Difficulty = question.Difficulty,
            AskedAt = now
        };
        db.InterviewTurns.Add(turn);
        await db.SaveChangesAsync(ct);

        events.RaiseQuestionAdvanced(new QuestionAdvanced(session.Id, turn.SequenceNo, template.MaxQuestions, turn.Category));
        return new NextQuestionResult(false, question.Text, question.Category, turn.SequenceNo, template.MaxQuestions, null);
    }

    /// <summary>
    /// Prefers the current difficulty and a category not yet covered; falls back to the nearest
    /// difficulty so a thin bank does not end the interview early.
    /// </summary>
    private async Task<Question?> PickQuestionAsync(InterviewSession session, CancellationToken ct)
    {
        var askedIds = session.Turns.Where(t => t.QuestionId != null).Select(t => t.QuestionId!.Value).ToList();
        var coveredCategories = session.Turns.Select(t => t.Category).ToHashSet();

        var candidates = await db.Questions
            .Where(q => q.TemplateId == session.TemplateId && q.IsActive && !askedIds.Contains(q.Id))
            .ToListAsync(ct);

        return candidates
            .OrderBy(q => Math.Abs((int)q.Difficulty - (int)session.CurrentDifficulty))
            .ThenBy(q => coveredCategories.Contains(q.Category) ? 1 : 0)
            .ThenBy(_ => Random.Shared.Next())
            .FirstOrDefault();
    }

    public async Task RecordUtteranceAsync(Guid sessionId, int? turnSequenceNo, Speaker speaker, string text, CancellationToken ct)
    {
        var turn = await db.InterviewTurns
            .Where(t => t.SessionId == sessionId && (turnSequenceNo == null || t.SequenceNo == turnSequenceNo))
            .OrderByDescending(t => t.SequenceNo)
            .FirstOrDefaultAsync(ct);

        // Small talk before the first question is deliberately not stored.
        if (turn is null) return;

        turn.AddUtterance(speaker, text, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    public Task<int?> CurrentTurnNumberAsync(Guid sessionId, CancellationToken ct) =>
        db.InterviewTurns.Where(t => t.SessionId == sessionId).MaxAsync(t => (int?)t.SequenceNo, ct);

    /// <summary>Idempotent: safe to call from the candidate, the model's end_interview tool and the time limit.</summary>
    public async Task FinishAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.InterviewSessions.Include(s => s.Turns).FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InterviewNotFoundException();
        if (!session.IsLive) return;

        session.Complete(clock.GetUtcNow());
        db.AuditEvents.Add(new AuditEvent { OrganizationId = session.OrganizationId, ActorUserId = session.CandidateId, Action = "interview.finished", EntityId = session.Id.ToString() });
        await db.SaveChangesAsync(ct);

        events.RaiseSessionEnded(session.Id);
        if (session.Status == InterviewStatus.Completed)
            await reportQueue.EnqueueAsync(session.Id, ct);
    }
}

public sealed record NextQuestionResult(bool InterviewOver, string? Question, string? Category, int Number, int Max, string? Reason)
{
    public static NextQuestionResult Over(string reason) => new(true, null, null, 0, 0, reason);
}

public class InterviewNotFoundException() : Exception("Interview not found.");

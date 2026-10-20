using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoiceHire.Application.Abstractions;
using VoiceHire.Application.Assessments;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Rules;

namespace VoiceHire.Application.Reports;

public class ReportGenerator(
    IAppDbContext db,
    IAnswerAssessmentService assessor,
    IInterviewEvents events,
    ILogger<ReportGenerator> logger)
{
    private const decimal StrengthThreshold = 70;
    private const decimal GapThreshold = 50;

    public async Task GenerateAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.InterviewSessions
            .Include(s => s.Template)
            .Include(s => s.Rubric)
            .Include(s => s.Turns).ThenInclude(t => t.Assessment)
            .Include(s => s.Report)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null || session.Report is not null) return;

        session.MarkEvaluating();
        await db.SaveChangesAsync(ct);

        try
        {
            var questionIds = session.Turns.Where(t => t.QuestionId != null).Select(t => t.QuestionId!.Value).ToList();
            var concepts = await db.Questions.Where(q => questionIds.Contains(q.Id))
                .ToDictionaryAsync(q => q.Id, q => q.ExpectedConcepts, ct);

            // Turns are assessed and saved one at a time so a failure part-way can resume.
            foreach (var turn in session.Turns.Where(t => t.Assessment is null).OrderBy(t => t.SequenceNo))
            {
                var assessment = turn.HasCandidateAnswer
                    ? await AssessTurnAsync(session, turn, concepts.GetValueOrDefault(turn.QuestionId ?? Guid.Empty) ?? [], ct)
                    : NoAnswerAssessment(session.Rubric.Criteria);
                assessment.TurnId = turn.Id;
                // Added explicitly: keys are client-generated, so EF would treat a navigation-only entity as existing.
                db.TurnAssessments.Add(assessment);
                await db.SaveChangesAsync(ct);
            }

            db.InterviewReports.Add(BuildReport(session));
            session.MarkEvaluated();
            await db.SaveChangesAsync(ct);
            events.RaiseReportReady(session.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Report generation failed for interview {SessionId}", sessionId);
            session.MarkEvaluationFailed();
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<TurnAssessment> AssessTurnAsync(InterviewSession session, InterviewTurn turn, IReadOnlyList<string> expectedConcepts, CancellationToken ct)
    {
        var criteria = session.Rubric.Criteria;
        var raw = await assessor.AssessAsync(new AnswerAssessmentRequest(
            session.Template.Role, turn.QuestionText, turn.Category, expectedConcepts, turn.Utterances, criteria), ct);
        var result = AssessmentValidator.Validate(raw, criteria, turn.Utterances);

        return new TurnAssessment
        {
            TurnId = turn.Id,
            Scores = criteria.Select(c => new CriterionScore { Criterion = c.Key, Score = result.Scores[c.Key] }).ToList(),
            Evidence = result.Evidence.ToList(),
            MissingConcepts = result.MissingConcepts.ToList(),
            Feedback = result.Feedback,
            NeedsHumanReview = result.NeedsHumanReview,
            WeightedScore = ScoreCalculator.WeightedScore(criteria, result.Scores),
            ModelVersion = result.ModelVersion
        };
    }

    /// <summary>No transcript may mean a speech-recognition failure, not a lack of knowledge, so it is always reviewed.</summary>
    private static TurnAssessment NoAnswerAssessment(IReadOnlyList<RubricCriterion> criteria) => new()
    {
        Scores = criteria.Select(c => new CriterionScore { Criterion = c.Key, Score = ScoreCalculator.MinCriterionScore }).ToList(),
        Feedback = "No answer was captured for this question.",
        NeedsHumanReview = true,
        WeightedScore = 0,
        ModelVersion = "none"
    };

    private static InterviewReport BuildReport(InterviewSession session)
    {
        var assessed = session.Turns.Where(t => t.Assessment is not null).ToList();
        var total = assessed.Count == 0 ? 0 : Math.Round(assessed.Average(t => t.Assessment!.WeightedScore), 1);

        var byCategory = assessed
            .GroupBy(t => t.Category)
            .Select(g => (Category: g.Key, Score: g.Average(t => t.Assessment!.WeightedScore)))
            .ToList();

        var gaps = byCategory.Where(c => c.Score < GapThreshold).Select(c => $"{c.Category} ({c.Score:0})")
            .Concat(assessed.SelectMany(t => t.Assessment!.MissingConcepts).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
            .ToList();

        return new InterviewReport
        {
            SessionId = session.Id,
            TotalScore = total,
            RubricVersion = session.Rubric.Version,
            Strengths = byCategory.Where(c => c.Score >= StrengthThreshold).Select(c => $"{c.Category} ({c.Score:0})").ToList(),
            Gaps = gaps,
            NeedsHumanReview = assessed.Any(t => t.Assessment!.NeedsHumanReview),
            Summary = $"{assessed.Count} question(s) assessed against rubric v{session.Rubric.Version}. " +
                      $"Overall {total:0.#}/100. This is a coaching signal, not a hiring decision."
        };
    }
}

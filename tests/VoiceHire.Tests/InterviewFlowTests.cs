using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceHire.Application.Abstractions;
using VoiceHire.Application.Assessments;
using VoiceHire.Application.Interviews;
using VoiceHire.Application.Reports;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Enums;
using VoiceHire.Infrastructure.Background;
using VoiceHire.Infrastructure.Identity;
using VoiceHire.Infrastructure.Persistence;

namespace VoiceHire.Tests;

public sealed class InterviewFlowTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private VoiceHireDbContext _db = null!;
    private readonly FakeReportQueue _queue = new();
    private readonly InterviewEvents _events = new();
    private Guid _templateId;
    private const string CandidateId = "candidate-1";
    private const string OutsiderId = "outsider-1";

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new VoiceHireDbContext(new DbContextOptionsBuilder<VoiceHireDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedAsync(_db);

        _db.Users.AddRange(new ApplicationUser { Id = CandidateId, UserName = "c" }, new ApplicationUser { Id = OutsiderId, UserName = "o" });
        var org = await _db.Organizations.SingleAsync();
        _db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org.Id, UserId = CandidateId });
        await _db.SaveChangesAsync();
        _templateId = (await _db.InterviewTemplates.SingleAsync()).Id;
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private InterviewOrchestrator Orchestrator() =>
        new(_db, new InterviewAccess(_db), _queue, _events, TimeProvider.System);

    [Fact]
    public async Task Non_members_cannot_start_an_interview()
    {
        await Assert.ThrowsAsync<InterviewNotFoundException>(() => Orchestrator().CreateSessionAsync(OutsiderId, _templateId, default));
    }

    [Fact]
    public async Task Questions_adapt_and_stop_at_the_template_limit()
    {
        var orchestrator = Orchestrator();
        var session = await orchestrator.CreateSessionAsync(CandidateId, _templateId, default);
        await orchestrator.AttachRealtimeCallAsync(session, "rtc_test", default);

        var first = await orchestrator.GetNextQuestionAsync(session.Id, null, default);
        Assert.False(first.InterviewOver);
        Assert.Equal(1, first.Number);

        var asked = new HashSet<string> { first.Question! };
        var max = (await _db.InterviewTemplates.SingleAsync()).MaxQuestions;
        for (var i = 2; i <= max; i++)
        {
            var next = await orchestrator.GetNextQuestionAsync(session.Id, AnswerSignal.Strong, default);
            Assert.False(next.InterviewOver);
            Assert.True(asked.Add(next.Question!), "A question was repeated.");
        }

        var turns = await _db.InterviewTurns.Where(t => t.SessionId == session.Id).OrderBy(t => t.SequenceNo).ToListAsync();
        Assert.Equal(Difficulty.Basic, turns[0].Difficulty);
        Assert.Equal(Difficulty.Intermediate, turns[1].Difficulty); // strong answers raise difficulty
        Assert.Equal(Difficulty.Advanced, turns[2].Difficulty);
        // Once the advanced questions run out, the nearest difficulty is used instead of ending early.
        Assert.Equal(max, turns.Count);

        var over = await orchestrator.GetNextQuestionAsync(session.Id, AnswerSignal.Strong, default);
        Assert.True(over.InterviewOver);
    }

    [Fact]
    public async Task Late_transcripts_attach_to_the_turn_they_answered_and_finish_queues_a_report()
    {
        var orchestrator = Orchestrator();
        var session = await orchestrator.CreateSessionAsync(CandidateId, _templateId, default);
        await orchestrator.AttachRealtimeCallAsync(session, "rtc_test", default);

        await orchestrator.GetNextQuestionAsync(session.Id, null, default);
        await orchestrator.GetNextQuestionAsync(session.Id, AnswerSignal.Partial, default);
        // The answer to question 1 is transcribed after question 2 was issued.
        await orchestrator.RecordUtteranceAsync(session.Id, 1, Speaker.Candidate, "answer to one", default);

        var turn1 = await _db.InterviewTurns.SingleAsync(t => t.SessionId == session.Id && t.SequenceNo == 1);
        Assert.Contains(turn1.Utterances, u => u.Text == "answer to one");

        await orchestrator.FinishAsync(session.Id, default);
        await orchestrator.FinishAsync(session.Id, default); // idempotent

        Assert.Equal([session.Id], _queue.Items);
        Assert.Equal(InterviewStatus.Completed, (await _db.InterviewSessions.SingleAsync(s => s.Id == session.Id)).Status);
    }

    [Fact]
    public async Task Interviews_without_answers_are_abandoned_not_evaluated()
    {
        var orchestrator = Orchestrator();
        var session = await orchestrator.CreateSessionAsync(CandidateId, _templateId, default);
        await orchestrator.GetNextQuestionAsync(session.Id, null, default);
        await orchestrator.FinishAsync(session.Id, default);

        Assert.Empty(_queue.Items);
        Assert.Equal(InterviewStatus.Abandoned, (await _db.InterviewSessions.SingleAsync(s => s.Id == session.Id)).Status);
    }

    [Fact]
    public async Task Report_score_is_calculated_from_criterion_scores_and_unanswered_turns_need_review()
    {
        var orchestrator = Orchestrator();
        var session = await orchestrator.CreateSessionAsync(CandidateId, _templateId, default);
        await orchestrator.AttachRealtimeCallAsync(session, "rtc_test", default);
        await orchestrator.GetNextQuestionAsync(session.Id, null, default);
        await orchestrator.RecordUtteranceAsync(session.Id, 1, Speaker.Candidate, "a full answer", default);
        await orchestrator.GetNextQuestionAsync(session.Id, AnswerSignal.Strong, default); // left unanswered
        await orchestrator.FinishAsync(session.Id, default);

        var generator = new ReportGenerator(_db, new FixedAssessor(5), _events, NullLogger<ReportGenerator>.Instance);
        await generator.GenerateAsync(session.Id, default);

        var report = await _db.InterviewReports.SingleAsync(r => r.SessionId == session.Id);
        Assert.Equal(50m, report.TotalScore); // (100 + 0) / 2
        Assert.True(report.NeedsHumanReview);
        Assert.Equal(InterviewStatus.Evaluated, (await _db.InterviewSessions.SingleAsync(s => s.Id == session.Id)).Status);
    }

    private sealed class FakeReportQueue : IReportQueue
    {
        public List<Guid> Items { get; } = [];
        public ValueTask EnqueueAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            Items.Add(sessionId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedAssessor(int score) : IAnswerAssessmentService
    {
        public Task<AnswerAssessmentResult> AssessAsync(AnswerAssessmentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AnswerAssessmentResult(
                request.Criteria.ToDictionary(c => c.Key, _ => score), [], [], "Good.", false, "fixed"));
    }
}

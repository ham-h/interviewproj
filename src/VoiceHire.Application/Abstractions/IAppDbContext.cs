using Microsoft.EntityFrameworkCore;
using VoiceHire.Domain.Entities;

namespace VoiceHire.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<OrganizationMember> OrganizationMembers { get; }
    DbSet<InterviewTemplate> InterviewTemplates { get; }
    DbSet<Question> Questions { get; }
    DbSet<ScoringRubric> ScoringRubrics { get; }
    DbSet<InterviewSession> InterviewSessions { get; }
    DbSet<InterviewTurn> InterviewTurns { get; }
    DbSet<TurnAssessment> TurnAssessments { get; }
    DbSet<InterviewReport> InterviewReports { get; }
    DbSet<AuditEvent> AuditEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

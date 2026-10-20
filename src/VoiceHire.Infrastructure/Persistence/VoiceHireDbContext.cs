using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VoiceHire.Application.Abstractions;
using VoiceHire.Domain.Entities;
using VoiceHire.Infrastructure.Identity;

namespace VoiceHire.Infrastructure.Persistence;

public class VoiceHireDbContext(DbContextOptions<VoiceHireDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IAppDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<InterviewTemplate> InterviewTemplates => Set<InterviewTemplate>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<ScoringRubric> ScoringRubrics => Set<ScoringRubric>();
    public DbSet<InterviewSession> InterviewSessions => Set<InterviewSession>();
    public DbSet<InterviewTurn> InterviewTurns => Set<InterviewTurn>();
    public DbSet<TurnAssessment> TurnAssessments => Set<TurnAssessment>();
    public DbSet<InterviewReport> InterviewReports => Set<InterviewReport>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Organization>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Members).WithOne().HasForeignKey(m => m.OrganizationId);
        });

        builder.Entity<OrganizationMember>(e =>
        {
            e.HasKey(x => new { x.OrganizationId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        });

        builder.Entity<InterviewTemplate>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Role).HasMaxLength(100);
            e.Property(x => x.StartingDifficulty).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(q => q.TemplateId);
            e.HasMany(x => x.Rubrics).WithOne().HasForeignKey(r => r.TemplateId);
        });

        builder.Entity<Question>(e =>
        {
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Difficulty).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SourceDocument).HasMaxLength(260);
            e.Property(x => x.CreatedBy).HasMaxLength(450);
            e.HasIndex(x => new { x.TemplateId, x.IsActive, x.Difficulty });
        });

        builder.Entity<ScoringRubric>(e =>
        {
            e.HasIndex(x => new { x.TemplateId, x.Version }).IsUnique();
            e.OwnsMany(x => x.Criteria, c => c.ToJson());
        });

        builder.Entity<InterviewSession>(e =>
        {
            e.Property(x => x.CandidateId).HasMaxLength(450);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CurrentDifficulty).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ProviderCallId).HasMaxLength(200);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Rubric).WithMany().HasForeignKey(x => x.RubricId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Turns).WithOne().HasForeignKey(t => t.SessionId);
            e.HasOne(x => x.Report).WithOne().HasForeignKey<InterviewReport>(r => r.SessionId);
            e.Ignore(x => x.CurrentTurn);
            e.HasIndex(x => new { x.CandidateId, x.CreatedAt });
            e.HasIndex(x => new { x.OrganizationId, x.Status, x.CreatedAt });
        });

        builder.Entity<InterviewTurn>(e =>
        {
            e.HasIndex(x => new { x.SessionId, x.SequenceNo }).IsUnique();
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Difficulty).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.InterviewerSignal).HasConversion<string>().HasMaxLength(20);
            e.OwnsMany(x => x.Utterances, u =>
            {
                u.ToJson();
                u.Property(p => p.Speaker).HasConversion<string>();
            });
            e.HasOne(x => x.Assessment).WithOne().HasForeignKey<TurnAssessment>(a => a.TurnId);
        });

        builder.Entity<TurnAssessment>(e =>
        {
            e.Property(x => x.WeightedScore).HasPrecision(5, 1);
            e.Property(x => x.ModelVersion).HasMaxLength(100);
            e.OwnsMany(x => x.Scores, s => s.ToJson());
            e.OwnsMany(x => x.Evidence, s => s.ToJson());
        });

        builder.Entity<InterviewReport>(e =>
        {
            e.Property(x => x.TotalScore).HasPrecision(5, 1);
            e.Property(x => x.AdjustedScore).HasPrecision(5, 1);
            e.Property(x => x.ReviewedBy).HasMaxLength(450);
            e.Ignore(x => x.EffectiveScore);
        });

        builder.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.ActorUserId).HasMaxLength(450);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        });
    }
}

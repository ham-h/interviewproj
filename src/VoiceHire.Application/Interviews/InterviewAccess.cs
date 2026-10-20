using Microsoft.EntityFrameworkCore;
using VoiceHire.Application.Abstractions;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Enums;

namespace VoiceHire.Application.Interviews;

public class InterviewAccess(IAppDbContext db)
{
    public Task<bool> IsMemberAsync(string userId, Guid organizationId, CancellationToken ct) =>
        db.OrganizationMembers.AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId, ct);

    public Task<bool> IsReviewerAsync(string userId, Guid organizationId, CancellationToken ct) =>
        db.OrganizationMembers.AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId
            && (m.Role == OrganizationRole.Reviewer || m.Role == OrganizationRole.Admin), ct);

    /// <summary>The candidate, or a reviewer/admin in the session's organization.</summary>
    public async Task<bool> CanViewAsync(string userId, InterviewSession session, CancellationToken ct) =>
        session.CandidateId == userId || await IsReviewerAsync(userId, session.OrganizationId, ct);
}

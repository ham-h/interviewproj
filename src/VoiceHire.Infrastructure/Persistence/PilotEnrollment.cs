using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Enums;

namespace VoiceHire.Infrastructure.Persistence;

public class PilotOptions
{
    public const string Section = "Pilot";

    /// <summary>When true, every signed-in user joins the pilot organization as a member.</summary>
    public bool AutoEnrollUsers { get; set; } = true;

    /// <summary>Emails enrolled as organization admins (can review every report).</summary>
    public List<string> AdminEmails { get; set; } = [];
}

/// <summary>Internal-pilot shortcut until invitations exist. Turn AutoEnrollUsers off for external candidates.</summary>
public class PilotEnrollment(VoiceHireDbContext db, IOptions<PilotOptions> options)
{
    public async Task EnsureEnrolledAsync(string userId, string? email, CancellationToken ct)
    {
        var pilot = options.Value;
        if (!pilot.AutoEnrollUsers) return;

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Name == DbSeeder.PilotOrganizationName, ct);
        if (org is null) return;

        var isAdmin = email is not null && pilot.AdminEmails.Contains(email, StringComparer.OrdinalIgnoreCase);
        var member = await db.OrganizationMembers.FindAsync([org.Id, userId], ct);
        if (member is null)
        {
            db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org.Id, UserId = userId, Role = isAdmin ? OrganizationRole.Admin : OrganizationRole.Member });
        }
        else if (isAdmin && member.Role != OrganizationRole.Admin)
        {
            member.Role = OrganizationRole.Admin;
        }
        else return;

        await db.SaveChangesAsync(ct);
    }
}

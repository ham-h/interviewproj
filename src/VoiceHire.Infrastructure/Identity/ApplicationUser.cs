using Microsoft.AspNetCore.Identity;

namespace VoiceHire.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
}

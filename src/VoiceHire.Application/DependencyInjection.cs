using Microsoft.Extensions.DependencyInjection;
using VoiceHire.Application.Interviews;
using VoiceHire.Application.QuestionBank;
using VoiceHire.Application.Reports;

namespace VoiceHire.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<InterviewAccess>();
        services.AddScoped<InterviewOrchestrator>();
        services.AddScoped<ReportGenerator>();
        services.AddScoped<QuestionImportService>();
        return services;
    }
}

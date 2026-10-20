using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VoiceHire.Application.Abstractions;
using VoiceHire.Application.Assessments;
using VoiceHire.Application.QuestionBank;
using VoiceHire.Infrastructure.Documents;
using VoiceHire.Infrastructure.AI;
using VoiceHire.Infrastructure.Background;
using VoiceHire.Infrastructure.Persistence;

namespace VoiceHire.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<VoiceHireDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<VoiceHireDbContext>());
        services.AddScoped<PilotEnrollment>();
        services.Configure<PilotOptions>(configuration.GetSection(PilotOptions.Section));

        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.Section));
        services.AddHttpClient<IRealtimeInterviewProvider, OpenAiRealtimeProvider>((sp, c) =>
            OpenAiRealtimeProvider.Configure(c, sp.GetRequiredService<IOptions<OpenAiOptions>>().Value));
        services.AddHttpClient<IAnswerAssessmentService, OpenAiAnswerAssessmentService>((sp, c) =>
        {
            OpenAiRealtimeProvider.Configure(c, sp.GetRequiredService<IOptions<OpenAiOptions>>().Value);
            c.Timeout = TimeSpan.FromMinutes(2);
        });

        services.AddHttpClient<IQuestionExtractor, OpenAiQuestionExtractor>((sp, c) =>
        {
            OpenAiRealtimeProvider.Configure(c, sp.GetRequiredService<IOptions<OpenAiOptions>>().Value);
            c.Timeout = TimeSpan.FromMinutes(3);
        });
        services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();

        services.AddSingleton<RealtimeCallMonitor>();
        services.AddSingleton<IRealtimeCallMonitor>(sp => sp.GetRequiredService<RealtimeCallMonitor>());
        services.AddHostedService(sp => sp.GetRequiredService<RealtimeCallMonitor>());

        services.AddSingleton<IInterviewEvents, InterviewEvents>();
        services.AddSingleton<ReportQueue>();
        services.AddSingleton<IReportQueue>(sp => sp.GetRequiredService<ReportQueue>());
        services.AddHostedService<ReportWorker>();

        return services;
    }
}

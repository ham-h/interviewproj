using Microsoft.EntityFrameworkCore;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Enums;
using VoiceHire.Domain.Rules;

namespace VoiceHire.Infrastructure.Persistence;

/// <summary>Seeds the internal pilot organization and the Junior .NET Developer template.</summary>
public static class DbSeeder
{
    public const string PilotOrganizationName = "Internal Pilot";

    public static async Task SeedAsync(VoiceHireDbContext db, CancellationToken ct = default)
    {
        if (await db.Organizations.AnyAsync(ct)) return;

        var org = new Organization { Name = PilotOrganizationName };
        var template = new InterviewTemplate
        {
            OrganizationId = org.Id,
            Title = "Junior .NET Developer",
            Role = "Junior .NET Developer",
            Description = "C#, ASP.NET Core, EF Core and SQL, Blazor, and practical debugging. About 15–20 minutes.",
            StartingDifficulty = Difficulty.Basic,
            MaxQuestions = 7,
            MaxDurationMinutes = 20,
            IsPublished = true,
            Rubrics =
            [
                new ScoringRubric { Version = 1, Criteria = DefaultRubric.Criteria() }
            ],
            Questions =
            [
                Q("C#", Difficulty.Basic, "What is the difference between a value type and a reference type in C#?", "stack vs heap is an implementation detail", "copy semantics", "struct vs class", "null"),
                Q("C#", Difficulty.Basic, "What is the difference between an abstract class and an interface in C#?", "single vs multiple inheritance", "state and constructors", "default interface methods", "when to choose each"),
                Q("C#", Difficulty.Intermediate, "Explain how async and await work in C#, and what problem they solve.", "non-blocking I/O", "Task", "continuations", "thread not blocked", "ConfigureAwait or sync context"),
                Q("C#", Difficulty.Intermediate, "What is the difference between IEnumerable and IQueryable?", "in-memory vs provider translation", "expression trees", "deferred execution", "where filtering runs"),
                Q("C#", Difficulty.Advanced, "What can go wrong with async void methods and with blocking on .Result, and how do you avoid it?", "unobserved exceptions", "deadlocks", "async all the way", "event handlers exception"),
                Q("ASP.NET Core", Difficulty.Basic, "What is dependency injection, and how is it used in ASP.NET Core?", "constructor injection", "service registration", "loose coupling", "testability"),
                Q("ASP.NET Core", Difficulty.Intermediate, "Explain the transient, scoped and singleton service lifetimes, with an example of each.", "per resolution", "per request", "app lifetime", "captive dependency"),
                Q("ASP.NET Core", Difficulty.Intermediate, "What is middleware in ASP.NET Core, and why does the order matter?", "request pipeline", "next delegate", "authentication before authorization", "short-circuiting"),
                Q("ASP.NET Core", Difficulty.Advanced, "A scoped DbContext is injected into a singleton background service and fails under load. Why, and how would you fix it?", "captive dependency", "DbContext not thread-safe", "IServiceScopeFactory", "create scope per unit of work"),
                Q("EF Core & SQL", Difficulty.Basic, "What is the difference between an INNER JOIN and a LEFT JOIN?", "matching rows only", "all left rows", "nulls for unmatched"),
                Q("EF Core & SQL", Difficulty.Intermediate, "What is the N+1 query problem, and how do you avoid it in EF Core?", "lazy loading", "Include", "projection", "split queries", "logging SQL"),
                Q("EF Core & SQL", Difficulty.Advanced, "How would you investigate and fix a slow EF Core query in production?", "logging generated SQL", "execution plan", "indexes", "AsNoTracking", "projection"),
                Q("Blazor", Difficulty.Basic, "What is the difference between Blazor Server and Blazor WebAssembly?", "where code runs", "SignalR circuit", "download size", "offline"),
                Q("Blazor", Difficulty.Intermediate, "How do components communicate in Blazor?", "parameters", "EventCallback", "cascading values", "state container service"),
                Q("Debugging", Difficulty.Intermediate, "An API endpoint returns HTTP 500 only in production. Walk me through how you would find the cause.", "logs and correlation id", "reproduce", "configuration differences", "exception details not exposed"),
                Q("Debugging", Difficulty.Advanced, "Memory usage of an ASP.NET Core service keeps growing over days. How would you diagnose it?", "dotnet-counters", "memory dump", "static or singleton caches", "event handler leaks", "IDisposable")
            ]
        };

        db.Organizations.Add(org);
        db.InterviewTemplates.Add(template);
        await db.SaveChangesAsync(ct);
    }

    private static Question Q(string category, Difficulty difficulty, string text, params string[] concepts) =>
        new() { Category = category, Difficulty = difficulty, Text = text, ExpectedConcepts = [.. concepts], Source = QuestionSource.Seeded };
}

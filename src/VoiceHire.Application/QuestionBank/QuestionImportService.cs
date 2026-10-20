using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VoiceHire.Application.Abstractions;
using VoiceHire.Application.Interviews;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Enums;
using VoiceHire.Domain.Rules;

namespace VoiceHire.Application.QuestionBank;

/// <summary>
/// Lets a reviewer turn their own PDF/Word question list into a template's question bank.
/// Extraction produces drafts only; nothing reaches the bank until a reviewer confirms it.
/// </summary>
public partial class QuestionImportService(
    IAppDbContext db,
    InterviewAccess access,
    IDocumentTextExtractor textExtractor,
    IQuestionExtractor questionExtractor,
    TimeProvider clock)
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    public const int MaxDocumentChars = 120_000;
    private const int ChunkChars = 15_000;

    public IReadOnlyCollection<string> SupportedExtensions => textExtractor.SupportedExtensions;

    /// <summary>Templates in organizations where the user can manage questions.</summary>
    public async Task<List<InterviewTemplate>> ManageableTemplatesAsync(string userId, CancellationToken ct)
    {
        var orgIds = await ManageableOrganizationIdsAsync(userId, ct);
        return await db.InterviewTemplates.AsNoTracking()
            .Where(t => orgIds.Contains(t.OrganizationId))
            .OrderBy(t => t.Title)
            .ToListAsync(ct);
    }

    public Task<List<Guid>> ManageableOrganizationIdsAsync(string userId, CancellationToken ct) =>
        db.OrganizationMembers
            .Where(m => m.UserId == userId && (m.Role == OrganizationRole.Reviewer || m.Role == OrganizationRole.Admin))
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

    public async Task<InterviewTemplate> CreateTemplateAsync(string userId, Guid organizationId, string title, string role, int maxQuestions, int maxDurationMinutes, CancellationToken ct)
    {
        if (!await access.IsReviewerAsync(userId, organizationId, ct)) throw new InterviewNotFoundException();

        var template = new InterviewTemplate
        {
            OrganizationId = organizationId,
            Title = title.Trim(),
            Role = role.Trim(),
            MaxQuestions = Math.Clamp(maxQuestions, 1, 20),
            MaxDurationMinutes = Math.Clamp(maxDurationMinutes, 5, 60),
            IsPublished = false, // published once it has questions
            Rubrics = [new ScoringRubric { Version = 1, Criteria = DefaultRubric.Criteria() }]
        };
        db.InterviewTemplates.Add(template);
        db.AuditEvents.Add(new AuditEvent { OrganizationId = organizationId, ActorUserId = userId, Action = "template.created", EntityId = template.Id.ToString() });
        await db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<ImportDraft> ExtractDraftsAsync(string userId, Guid templateId, Stream content, string fileName, CancellationToken ct)
    {
        var template = await AuthorizeAsync(userId, templateId, ct);

        var text = await textExtractor.ExtractTextAsync(content, fileName, ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new UnsupportedDocumentException("No text was found in this document. Scanned PDFs (images of text) are not supported yet.");
        var truncated = text.Length > MaxDocumentChars;
        if (truncated) text = text[..MaxDocumentChars];

        var extracted = new List<ExtractedQuestion>();
        foreach (var chunk in Chunk(text))
            extracted.AddRange(await questionExtractor.ExtractAsync(chunk, template.Role, ct));

        var normalizedDocument = Normalize(text);
        var existing = (await db.Questions.Where(q => q.TemplateId == templateId).Select(q => q.Text).ToListAsync(ct))
            .Select(Normalize).ToHashSet();

        var seen = new HashSet<string>();
        var drafts = new List<QuestionDraft>();
        foreach (var q in extracted)
        {
            var key = Normalize(q.Text);
            if (key.Length == 0 || !seen.Add(key)) continue;

            var found = normalizedDocument.Contains(key);
            var duplicate = existing.Contains(key);
            drafts.Add(new QuestionDraft
            {
                Text = q.Text.Trim(),
                Category = q.Category.Trim(),
                Difficulty = q.Difficulty,
                ExpectedConcepts = string.Join(", ", q.ExpectedConcepts),
                ConceptsFromDocument = q.ConceptsFromDocument,
                FoundInDocument = found,
                AlreadyInBank = duplicate,
                Include = found && !duplicate
            });
        }

        return new ImportDraft(fileName, drafts, truncated);
    }

    public async Task<int> SaveAsync(string userId, Guid templateId, string fileName, IEnumerable<QuestionDraft> drafts, CancellationToken ct)
    {
        var template = await AuthorizeAsync(userId, templateId, ct);
        var now = clock.GetUtcNow();

        var questions = drafts
            .Where(d => d.Include && !string.IsNullOrWhiteSpace(d.Text) && !string.IsNullOrWhiteSpace(d.Category))
            .Select(d => new Question
            {
                TemplateId = templateId,
                Text = d.Text.Trim(),
                Category = d.Category.Trim(),
                Difficulty = d.Difficulty,
                ExpectedConcepts = d.ExpectedConcepts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Source = QuestionSource.Imported,
                SourceDocument = Path.GetFileName(fileName),
                CreatedBy = userId,
                CreatedAt = now
            })
            .ToList();

        if (questions.Count == 0) return 0;

        db.Questions.AddRange(questions);
        if (!template.IsPublished) template.IsPublished = true;
        db.AuditEvents.Add(new AuditEvent { OrganizationId = template.OrganizationId, ActorUserId = userId, Action = $"questions.imported:{questions.Count}", EntityId = templateId.ToString() });
        await db.SaveChangesAsync(ct);
        return questions.Count;
    }

    public async Task<List<Question>> QuestionsAsync(string userId, Guid templateId, CancellationToken ct)
    {
        await AuthorizeAsync(userId, templateId, ct);
        return await db.Questions.AsNoTracking()
            .Where(q => q.TemplateId == templateId)
            .OrderBy(q => q.Category).ThenBy(q => q.Difficulty)
            .ToListAsync(ct);
    }

    public async Task SetActiveAsync(string userId, Guid questionId, bool active, CancellationToken ct)
    {
        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId, ct) ?? throw new InterviewNotFoundException();
        await AuthorizeAsync(userId, question.TemplateId, ct);
        question.IsActive = active;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetPublishedAsync(string userId, Guid templateId, bool published, CancellationToken ct)
    {
        var template = await AuthorizeAsync(userId, templateId, ct);
        template.IsPublished = published;
        await db.SaveChangesAsync(ct);
    }

    private async Task<InterviewTemplate> AuthorizeAsync(string userId, Guid templateId, CancellationToken ct)
    {
        var template = await db.InterviewTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null || !await access.IsReviewerAsync(userId, template.OrganizationId, ct))
            throw new InterviewNotFoundException();
        return template;
    }

    /// <summary>Splits on paragraph boundaries so a question is never cut in half between chunks.</summary>
    internal static IEnumerable<string> Chunk(string text)
    {
        var current = new StringBuilder();
        foreach (var paragraph in text.Split('\n'))
        {
            if (current.Length > 0 && current.Length + paragraph.Length > ChunkChars)
            {
                yield return current.ToString();
                current.Clear();
            }
            current.Append(paragraph).Append('\n');
        }
        if (current.Length > 0) yield return current.ToString();
    }

    /// <summary>Case-, punctuation- and whitespace-insensitive form, used for the verbatim and duplicate checks.</summary>
    internal static string Normalize(string text) =>
        Whitespace().Replace(NonWord().Replace(text.ToLowerInvariant(), " "), " ").Trim();

    [GeneratedRegex(@"[^\w]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

public sealed record ImportDraft(string FileName, List<QuestionDraft> Questions, bool Truncated);

/// <summary>Editable in the review screen before anything is saved.</summary>
public sealed class QuestionDraft
{
    public bool Include { get; set; }
    public required string Text { get; set; }
    public required string Category { get; set; }
    public Difficulty Difficulty { get; set; }
    public string ExpectedConcepts { get; set; } = "";

    /// <summary>False means the model suggested the concepts because the document had no model answer.</summary>
    public bool ConceptsFromDocument { get; init; }

    /// <summary>False means the text doesn't appear verbatim in the document: the model may have reworded or invented it.</summary>
    public bool FoundInDocument { get; init; }
    public bool AlreadyInBank { get; init; }
}

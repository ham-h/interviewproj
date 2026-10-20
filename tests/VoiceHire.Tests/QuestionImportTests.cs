using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VoiceHire.Application.Interviews;
using VoiceHire.Application.QuestionBank;
using VoiceHire.Domain.Entities;
using VoiceHire.Domain.Enums;
using VoiceHire.Infrastructure.Documents;
using VoiceHire.Infrastructure.Identity;
using VoiceHire.Infrastructure.Persistence;

namespace VoiceHire.Tests;

public class DocumentTextExtractorTests
{
    private readonly DocumentTextExtractor _extractor = new();

    [Fact]
    public async Task Reads_docx_paragraphs_and_table_cells_as_lines()
    {
        var docx = BuildDocx(
            "<w:p><w:r><w:t>Section: SQL</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t xml:space=\"preserve\">1. What is an </w:t></w:r><w:r><w:t>index?</w:t></w:r></w:p>" +
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Explain a deadlock.</w:t></w:r></w:p></w:tc></w:tr></w:tbl>");

        var text = await _extractor.ExtractTextAsync(new MemoryStream(docx), "questions.docx", default);

        Assert.Equal(["Section: SQL", "1. What is an index?", "Explain a deadlock."], text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("old.doc", "save it as .docx")]
    [InlineData("image.png", "PDF, Word")]
    public async Task Rejects_unsupported_files_with_a_helpful_message(string fileName, string expected)
    {
        var ex = await Assert.ThrowsAsync<UnsupportedDocumentException>(() => _extractor.ExtractTextAsync(new MemoryStream([1, 2, 3]), fileName, default));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Rejects_corrupt_pdf_and_docx()
    {
        await Assert.ThrowsAsync<UnsupportedDocumentException>(() => _extractor.ExtractTextAsync(new MemoryStream("not a pdf"u8.ToArray()), "x.pdf", default));
        await Assert.ThrowsAsync<UnsupportedDocumentException>(() => _extractor.ExtractTextAsync(new MemoryStream("not a zip"u8.ToArray()), "x.docx", default));
    }

    internal static byte[] BuildDocx(string bodyXml)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), Encoding.UTF8);
            writer.Write($"""<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{bodyXml}</w:body></w:document>""");
        }
        return ms.ToArray();
    }
}

public sealed class QuestionImportServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private VoiceHireDbContext _db = null!;
    private Guid _orgId;
    private const string ReviewerId = "reviewer-1";
    private const string MemberId = "member-1";

    private const string Document = """
        Backend screening questions
        Q1. What is the difference between IEnumerable and IQueryable?
        Model answer: IQueryable is translated by the provider (expression trees); IEnumerable runs in memory.
        Q2. How would you secure a public REST API?
        """;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new VoiceHireDbContext(new DbContextOptionsBuilder<VoiceHireDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedAsync(_db);

        _db.Users.AddRange(new ApplicationUser { Id = ReviewerId, UserName = "r" }, new ApplicationUser { Id = MemberId, UserName = "m" });
        _orgId = (await _db.Organizations.SingleAsync()).Id;
        _db.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = _orgId, UserId = ReviewerId, Role = OrganizationRole.Reviewer },
            new OrganizationMember { OrganizationId = _orgId, UserId = MemberId, Role = OrganizationRole.Member });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private QuestionImportService Service(params ExtractedQuestion[] extracted) =>
        new(_db, new InterviewAccess(_db), new DocumentTextExtractor(), new FakeExtractor(extracted), TimeProvider.System);

    private static Stream Txt(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Members_cannot_manage_questions()
    {
        var service = Service();
        await Assert.ThrowsAsync<InterviewNotFoundException>(() => service.CreateTemplateAsync(MemberId, _orgId, "T", "R", 5, 15, default));
        Assert.Empty(await service.ManageableOrganizationIdsAsync(MemberId, default));
    }

    [Fact]
    public async Task Drafts_flag_invented_questions_and_duplicates_and_only_selected_ones_are_saved()
    {
        var service = Service(
            new ExtractedQuestion("What is the difference between IEnumerable and IQueryable?", "C#", Difficulty.Intermediate, ["provider translation", "in-memory"], true),
            new ExtractedQuestion("How would you secure a public REST API?", "Security", Difficulty.Advanced, ["authentication", "rate limiting"], false),
            new ExtractedQuestion("How WOULD you secure a public REST API", "Security", Difficulty.Advanced, [], false), // duplicate after normalisation
            new ExtractedQuestion("What is a monad?", "FP", Difficulty.Advanced, [], false));                            // not in the document

        var template = await service.CreateTemplateAsync(ReviewerId, _orgId, "Backend screening", "Backend developer", 5, 15, default);
        Assert.False(template.IsPublished);

        var draft = await service.ExtractDraftsAsync(ReviewerId, template.Id, Txt(Document), "screening.txt", default);

        Assert.Equal(3, draft.Questions.Count);
        var invented = draft.Questions.Single(q => q.Text == "What is a monad?");
        Assert.False(invented.FoundInDocument);
        Assert.False(invented.Include);
        Assert.All(draft.Questions.Where(q => q != invented), q => Assert.True(q.Include));

        var added = await service.SaveAsync(ReviewerId, template.Id, draft.FileName, draft.Questions, default);
        Assert.Equal(2, added);

        var saved = await _db.Questions.Where(q => q.TemplateId == template.Id).ToListAsync();
        Assert.All(saved, q =>
        {
            Assert.Equal(QuestionSource.Imported, q.Source);
            Assert.Equal("screening.txt", q.SourceDocument);
        });
        Assert.True((await _db.InterviewTemplates.SingleAsync(t => t.Id == template.Id)).IsPublished);
        Assert.Single((await _db.ScoringRubrics.Where(r => r.TemplateId == template.Id).ToListAsync()));

        // Importing the same document again finds nothing new to add.
        var again = await service.ExtractDraftsAsync(ReviewerId, template.Id, Txt(Document), "screening.txt", default);
        Assert.All(again.Questions.Where(q => q.FoundInDocument), q => Assert.True(q.AlreadyInBank));
    }

    [Fact]
    public async Task Imported_questions_are_what_the_interviewer_asks()
    {
        var service = Service(new ExtractedQuestion("How would you secure a public REST API?", "Security", Difficulty.Basic, ["authentication"], false));
        var template = await service.CreateTemplateAsync(ReviewerId, _orgId, "Security", "Backend developer", 3, 15, default);
        var draft = await service.ExtractDraftsAsync(ReviewerId, template.Id, Txt(Document), "doc.txt", default);
        await service.SaveAsync(ReviewerId, template.Id, draft.FileName, draft.Questions, default);

        var orchestrator = new InterviewOrchestrator(_db, new InterviewAccess(_db), new NullQueue(), new VoiceHire.Infrastructure.Background.InterviewEvents(), TimeProvider.System);
        var session = await orchestrator.CreateSessionAsync(MemberId, template.Id, default);
        var first = await orchestrator.GetNextQuestionAsync(session.Id, null, default);
        var second = await orchestrator.GetNextQuestionAsync(session.Id, VoiceHire.Domain.Enums.AnswerSignal.Strong, default);

        Assert.Equal("How would you secure a public REST API?", first.Question);
        Assert.True(second.InterviewOver); // the bank only has one question
    }

    [Fact]
    public void Chunks_split_on_paragraph_boundaries()
    {
        var text = string.Join('\n', Enumerable.Range(0, 3000).Select(i => $"Question number {i}?"));
        var chunks = QuestionImportService.Chunk(text).ToList();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.EndsWith("?\n", c));
        Assert.Equal(text + "\n", string.Concat(chunks));
    }

    private sealed class FakeExtractor(ExtractedQuestion[] questions) : IQuestionExtractor
    {
        public Task<IReadOnlyList<ExtractedQuestion>> ExtractAsync(string documentText, string role, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExtractedQuestion>>(questions);
    }

    private sealed class NullQueue : VoiceHire.Application.Abstractions.IReportQueue
    {
        public ValueTask EnqueueAsync(Guid sessionId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

using VoiceHire.Domain.Enums;

namespace VoiceHire.Application.QuestionBank;

/// <summary>Turns an uploaded document into plain text. The document itself is never stored.</summary>
public interface IDocumentTextExtractor
{
    IReadOnlyCollection<string> SupportedExtensions { get; }
    Task<string> ExtractTextAsync(Stream content, string fileName, CancellationToken cancellationToken);
}

/// <summary>Finds the interview questions in a document's text and classifies them.</summary>
public interface IQuestionExtractor
{
    Task<IReadOnlyList<ExtractedQuestion>> ExtractAsync(string documentText, string role, CancellationToken cancellationToken);
}

public sealed record ExtractedQuestion(
    string Text,
    string Category,
    Difficulty Difficulty,
    IReadOnlyList<string> ExpectedConcepts,
    bool ConceptsFromDocument);

public class UnsupportedDocumentException(string message) : Exception(message);

using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using VoiceHire.Application.QuestionBank;

namespace VoiceHire.Infrastructure.Documents;

/// <summary>Reads text from PDF (PdfPig), Word .docx (Open XML read directly) and plain-text files.</summary>
public class DocumentTextExtractor : IDocumentTextExtractor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const long MaxUncompressedDocxXmlBytes = 50 * 1024 * 1024;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pdf", ".docx", ".txt"];

    public async Task<string> ExtractTextAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        // Both parsers need a seekable stream; uploads from Blazor are not.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => ReadPdf(buffer),
            ".docx" => ReadDocx(buffer),
            ".txt" => Encoding.UTF8.GetString(buffer.ToArray()),
            ".doc" => throw new UnsupportedDocumentException("Old .doc files aren't supported. Open the file in Word and save it as .docx."),
            _ => throw new UnsupportedDocumentException("Upload a PDF, Word (.docx) or text file.")
        };
    }

    private static string ReadPdf(Stream stream)
    {
        try
        {
            using var pdf = PdfDocument.Open(stream);
            var sb = new StringBuilder();
            foreach (var page in pdf.GetPages())
                sb.AppendLine(ContentOrderTextExtractor.GetText(page));
            return sb.ToString();
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new UnsupportedDocumentException("This PDF is password-protected. Remove the protection and upload it again.");
        }
        catch (Exception ex) when (ex is not UnsupportedDocumentException)
        {
            throw new UnsupportedDocumentException("This PDF couldn't be read. Try exporting it again, or save the questions as .docx.");
        }
    }

    private static string ReadDocx(Stream stream)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry("word/document.xml")
                ?? throw new UnsupportedDocumentException("This doesn't look like a Word document.");
            if (entry.Length > MaxUncompressedDocxXmlBytes)
                throw new UnsupportedDocumentException("This Word document is too large.");

            using var xml = XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            var body = XDocument.Load(xml).Root?.Element(W + "body");
            if (body is null) return "";

            // Every paragraph, including those inside tables, becomes one line.
            var sb = new StringBuilder();
            foreach (var paragraph in body.Descendants(W + "p"))
            {
                foreach (var node in paragraph.Descendants())
                {
                    if (node.Name == W + "t") sb.Append(node.Value);
                    else if (node.Name == W + "tab") sb.Append('\t');
                    else if (node.Name == W + "br") sb.Append('\n');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            throw new UnsupportedDocumentException("This Word document couldn't be read. Open it in Word and save it as .docx again.");
        }
    }
}

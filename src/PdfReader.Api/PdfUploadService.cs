using System.Text;
using UglyToad.PdfPig;

namespace PdfReader.Api;

public sealed record PdfProcessingResult(
    bool IsPdf,
    bool RequiresOcr,
    int PageCount,
    byte[] TextBytes,
    string? Error);

public sealed class PdfUploadService
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    public async Task<PdfProcessingResult> ProcessAsync(Stream upload, CancellationToken cancellationToken = default)
    {
        await using var bufferedUpload = new MemoryStream();
        await upload.CopyToAsync(bufferedUpload, cancellationToken);
        var pdfBytes = bufferedUpload.ToArray();

        if (!HasPdfSignature(pdfBytes))
        {
            return new(false, false, 0, [], "The upload does not start with the PDF signature %PDF-.");
        }

        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            var textBuilder = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                using var reader = new StringReader(page.Text);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    textBuilder.AppendLine(line);
                }
            }

            return new(true, textBuilder.Length == 0, document.NumberOfPages,
                Encoding.UTF8.GetBytes(textBuilder.ToString()), null);
        }
        catch (Exception)
        {
            return new(true, true, 0, [], "The PDF text could not be read and may require OCR.");
        }
    }

    private static bool HasPdfSignature(ReadOnlySpan<byte> bytes) => bytes.StartsWith(PdfSignature);
}
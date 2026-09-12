using System.Text;
using PdfReader.Api;

namespace PdfReader.Tests;

public sealed class PdfUploadServiceTests
{
    private readonly PdfUploadService service = new();

    [Fact]
    public async Task RejectsEmptyUpload()
    {
        var result = await service.ProcessAsync(new MemoryStream());

        Assert.False(result.IsPdf);
        Assert.False(result.RequiresOcr);
        Assert.Empty(result.TextBytes);
    }

    [Theory]
    [InlineData("PDF-1.7")]
    [InlineData("not a pdf")]
    public async Task RejectsUploadWithoutPdfSignature(string content)
    {
        var result = await service.ProcessAsync(ToStream(content));

        Assert.False(result.IsPdf);
        Assert.Contains("%PDF-", result.Error);
    }

    [Fact]
    public async Task ValidSignatureWithUnreadableContentRequestsOcr()
    {
        var result = await service.ProcessAsync(ToStream("%PDF-1.7\nnot a complete document"));

        Assert.True(result.IsPdf);
        Assert.True(result.RequiresOcr);
        Assert.Empty(result.TextBytes);
    }

    [Fact]
    public async Task ExtractsTextLineByLineIntoUtf8Bytes()
    {
        var result = await service.ProcessAsync(ToStream(ValidPdf));

        Assert.True(result.IsPdf);
        Assert.False(result.RequiresOcr);
        Assert.Equal(1, result.PageCount);
        Assert.Contains("Hello PDF", Encoding.UTF8.GetString(result.TextBytes));
    }

    private static MemoryStream ToStream(string content) => new(Encoding.ASCII.GetBytes(content));

    private const string ValidPdf = "%PDF-1.4\n" +
        "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
        "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
        "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 144] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n" +
        "4 0 obj\n<< /Length 43 >>\nstream\nBT /F1 24 Tf 10 100 Td (Hello PDF) Tj ET\nendstream\nendobj\n" +
        "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n" +
        "xref\n0 6\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n0000000115 00000 n \n0000000241 00000 n \n0000000331 00000 n \n" +
        "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n401\n%%EOF";
}
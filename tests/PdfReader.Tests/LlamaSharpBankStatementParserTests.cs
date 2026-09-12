using PdfReader.Api;

namespace PdfReader.Tests;

public sealed class LlamaSharpBankStatementParserTests
{
	[Fact]
	public async Task ReportsMissingModelConfiguration()
	{
		var parser = new LlamaSharpBankStatementParser(new LlamaSharpOptions());

		var exception = await Assert.ThrowsAsync<LlamaSharpParsingException>(() =>
			parser.ParseAsync("Account Number: 123"));

		Assert.Equal("model_not_configured", exception.Code);
	}
}
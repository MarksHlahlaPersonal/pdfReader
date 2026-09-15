using PdfReader.Api;

namespace PdfReader.Tests;

public sealed class SemanticKernelBankStatementParserTests
{
	[Fact]
	public async Task ReportsMissingEndpointConfiguration()
	{
		var parser = new SemanticKernelBankStatementParser(
			null!,
			new() { Endpoint = "" });

		var exception = await Assert.ThrowsAsync<SemanticKernelParsingException>(() =>
			parser.ParseAsync("Account Number: 123"));

		Assert.Equal("endpoint_not_configured", exception.Code);
	}
}
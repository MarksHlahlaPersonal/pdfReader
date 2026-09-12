using PdfReader.Api;

namespace PdfReader.Tests;

public sealed class BankStatementEndpointRulesTests
{
	[Fact]
	public void StatementWithNoRecognizedContentIsNotConsideredParsed()
	{
		var statement = new BankStatement(null, null, null, null, null, null, [], ["Not a statement"]);

		var hasRecognizedContent = statement.Transactions.Count > 0
			|| statement.AccountNumber is not null
			|| statement.AccountHolder is not null
			|| statement.StatementStartDate is not null
			|| statement.StatementEndDate is not null
			|| statement.OpeningBalance is not null
			|| statement.ClosingBalance is not null;

		Assert.False(hasRecognizedContent);
	}
}
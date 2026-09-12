using PdfReader.Api;

namespace PdfReader.Tests;

public sealed class BankStatementParserTests
{
    private readonly BankStatementParser parser = new();

    [Fact]
    public void ParsesStatementMetadataAndTransactions()
    {
        var statement = parser.Parse("""
            Account Number: 12345678
            Account Holder: Jane Doe
            Statement Period: 01/01/2026 - 31/01/2026
            Opening Balance: $1,000.00
            02/01/2026 Grocery Store -50.00 950.00
            05/01/2026 Salary 2,000.00 2,950.00
            Closing Balance: $2,950.00
            """);

        Assert.Equal("12345678", statement.AccountNumber);
        Assert.Equal("Jane Doe", statement.AccountHolder);
        Assert.Equal(new DateOnly(2026, 1, 1), statement.StatementStartDate);
        Assert.Equal(new DateOnly(2026, 1, 31), statement.StatementEndDate);
        Assert.Equal(1000m, statement.OpeningBalance);
        Assert.Equal(2950m, statement.ClosingBalance);
        Assert.Equal(2, statement.Transactions.Count);
        Assert.Equal(-50m, statement.Transactions[0].Amount);
        Assert.Equal(950m, statement.Transactions[0].Balance);
        Assert.Equal(2000m, statement.Transactions[1].Amount);
    }

    [Fact]
    public void KeepsUnknownLinesWithoutInventingTransactions()
    {
        var statement = parser.Parse("Bank statement\nTransaction date description amount\nNot a transaction");

        Assert.Empty(statement.Transactions);
        Assert.Equal(3, statement.UnparsedLines.Count);
    }

    [Fact]
    public void ParsesDebitMarkerAsNegativeAmount()
    {
        var statement = parser.Parse("15-02-2026 ATM withdrawal 25.00 DR 975.00");

        Assert.Single(statement.Transactions);
        Assert.Equal(-25m, statement.Transactions[0].Amount);
        Assert.Equal(975m, statement.Transactions[0].Balance);
    }
}
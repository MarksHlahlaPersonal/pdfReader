using System.Globalization;
using System.Text.RegularExpressions;

namespace PdfReader.Api;

public sealed record BankStatement(
    string? AccountNumber,
    string? AccountHolder,
    DateOnly? StatementStartDate,
    DateOnly? StatementEndDate,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    IReadOnlyList<BankTransaction> Transactions,
    IReadOnlyList<string> UnparsedLines);

public sealed record BankTransaction(
    DateOnly Date,
    string Description,
    decimal? Amount,
    decimal? Balance,
    string? Reference);

public sealed class BankStatementParser
{
    private static readonly Regex DatePrefix = new(
        @"^(?<date>\d{1,4}[./-]\d{1,2}[./-]\d{1,4})\s+(?<details>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Money = new(
        @"(?<!\w)[(+-]?\s*[$€£]?\s*\d{1,3}(?:[,.]\d{3})*(?:[,.]\d{2})\s*\)?(?:\s*(?:CR|DR))?(?!\w)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public BankStatement Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var transactions = new List<BankTransaction>();
        var unparsedLines = new List<string>();
        string? accountNumber = null;
        string? accountHolder = null;
        DateOnly? statementStartDate = null;
        DateOnly? statementEndDate = null;
        decimal? openingBalance = null;
        decimal? closingBalance = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var normalizedLine = line.TrimEnd('\r');
            if (TryReadMetadata(normalizedLine, ref accountNumber, ref accountHolder,
                    ref statementStartDate, ref statementEndDate, ref openingBalance, ref closingBalance))
            {
                continue;
            }

            if (TryParseTransaction(normalizedLine, out var transaction))
            {
                transactions.Add(transaction);
            }
            else
            {
                unparsedLines.Add(normalizedLine);
            }
        }

        return new(accountNumber, accountHolder, statementStartDate, statementEndDate,
            openingBalance, closingBalance, transactions, unparsedLines);
    }

    private static bool TryReadMetadata(
        string line,
        ref string? accountNumber,
        ref string? accountHolder,
        ref DateOnly? statementStartDate,
        ref DateOnly? statementEndDate,
        ref decimal? openingBalance,
        ref decimal? closingBalance)
    {
        var separatorIndex = line.IndexOf(':');
        if (separatorIndex < 0)
        {
            return false;
        }

        var label = line[..separatorIndex].Trim().ToLowerInvariant();
        var value = line[(separatorIndex + 1)..].Trim();

        switch (label)
        {
            case "account number":
            case "account no":
            case "account #":
                accountNumber = value;
                return true;
            case "account holder":
            case "customer name":
            case "name":
                accountHolder = value;
                return true;
            case "statement period":
                var dates = Regex.Matches(value, @"\d{1,4}[./-]\d{1,2}[./-]\d{1,4}");
                if (dates.Count >= 2)
                {
                    statementStartDate = ParseDate(dates[0].Value);
                    statementEndDate = ParseDate(dates[1].Value);
                }

                return true;
            case "opening balance":
                openingBalance = ParseMoney(value);
                return true;
            case "closing balance":
            case "ending balance":
                closingBalance = ParseMoney(value);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseTransaction(string line, out BankTransaction transaction)
    {
        transaction = default!;
        var dateMatch = DatePrefix.Match(line);
        if (!dateMatch.Success || !TryParseDate(dateMatch.Groups["date"].Value, out var date))
        {
            return false;
        }

        var details = dateMatch.Groups["details"].Value;
        var moneyMatches = Money.Matches(details);
        if (moneyMatches.Count == 0)
        {
            return false;
        }

        var amount = ParseMoney(moneyMatches[0].Value);
        decimal? balance = moneyMatches.Count > 1 ? ParseMoney(moneyMatches[1].Value) : null;
        var descriptionEnd = moneyMatches[0].Index;
        var description = details[..descriptionEnd].Trim();
        var reference = description.Length == 0 ? null : description;

        transaction = new(date, description, amount, balance, reference);
        return true;
    }

    private static DateOnly? ParseDate(string value) => TryParseDate(value, out var date) ? date : null;

    private static bool TryParseDate(string value, out DateOnly date)
    {
        var formats = new[] { "d/M/yyyy", "dd/MM/yyyy", "M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "d-M-yyyy", "dd-MM-yyyy" };
        return DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);
    }

    private static decimal? ParseMoney(string value)
    {
        var normalized = value.Trim().Replace("$", string.Empty).Replace("€", string.Empty).Replace("£", string.Empty);
        var isNegative = normalized.Contains('(') || normalized.Contains("DR", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith('-');
        normalized = normalized.Replace("(", string.Empty).Replace(")", string.Empty)
            .Replace("CR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("DR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty);

        if (normalized.Contains(',') && normalized.Contains('.') && normalized.LastIndexOf(',') < normalized.LastIndexOf('.'))
        {
            normalized = normalized.Replace(",", string.Empty);
        }
        else if (normalized.Contains(',') && !normalized.Contains('.'))
        {
            normalized = normalized.Replace(',', '.');
        }

        return decimal.TryParse(normalized.TrimStart('+', '-'), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var amount)
            ? isNegative ? -amount : amount
            : null;
    }
}
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace PdfReader.Api;

public sealed class SemanticKernelOptions
{
	public string Endpoint { get; set; } = "http://localhost:8080/v1";
	public string Model { get; set; } = "local-model";
	public string ApiKey { get; set; } = "no-key";
	public int MaxTokens { get; set; } = 2048;
	public int HttpClientTimeoutSeconds { get; set; } = 900;
}

public sealed class SemanticKernelParsingException(string code, string message) : Exception(message)
{
	public string Code { get; } = code;
}

public sealed class SemanticKernelBankStatementParser
{
	private readonly IChatCompletionService chatCompletionService;
	private readonly SemanticKernelOptions options;

	public SemanticKernelBankStatementParser(
		IChatCompletionService chatCompletionService,
		SemanticKernelOptions options)
	{
		this.chatCompletionService = chatCompletionService;
		this.options = options;
	}

	public async Task<BankStatement> ParseAsync(string extractedText, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(options.Endpoint))
		{
			throw new SemanticKernelParsingException("endpoint_not_configured",
				"Semantic Kernel is not configured. Set SemanticKernel:Endpoint to the local llama.cpp OpenAI API endpoint.");
		}

		if (string.IsNullOrWhiteSpace(options.Model))
		{
			throw new SemanticKernelParsingException("model_not_configured",
				"Semantic Kernel is not configured. Set SemanticKernel:Model to the local llama.cpp model name.");
		}

		try
		{
			var history = new ChatHistory();
			history.AddUserMessage(BuildPrompt(extractedText));
			var settings = new OpenAIPromptExecutionSettings
			{
				MaxTokens = options.MaxTokens,
				Temperature = 0,
				ResponseFormat = "json_object"
			};

			var response = await chatCompletionService.GetChatMessageContentAsync(
				history, settings, cancellationToken: cancellationToken);
			return DeserializeResponse(response.Content ?? string.Empty);
		}
		catch (SemanticKernelParsingException)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			throw new SemanticKernelParsingException("model_parse_failed",
				$"Semantic Kernel could not parse the bank statement: {exception.Message}");
		}
	}

	private static string BuildPrompt(string extractedText) => $$"""
You extract bank statement data. Return only valid JSON, with no markdown and no explanation.
Use this exact shape:
{
  "accountNumber": "string or null",
  "accountHolder": "string or null",
  "statementStartDate": "yyyy-MM-dd or null",
  "statementEndDate": "yyyy-MM-dd or null",
  "openingBalance": 0.0,
  "closingBalance": 0.0,
  "transactions": [
    { "date": "yyyy-MM-dd", "description": "string", "amount": 0.0, "balance": 0.0, "reference": "string or null" }
  ],
  "unparsedLines": ["string"]
}
Use null for unknown scalar values. Preserve uncertain source lines in unparsedLines. Do not invent transactions.

Statement text:
{{extractedText}}
""";

	private static BankStatement DeserializeResponse(string response)
	{
		var json = response.Trim();
		if (json.StartsWith("```", StringComparison.Ordinal))
		{
			var firstLineEnd = json.IndexOf('\n');
			var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
			if (firstLineEnd >= 0 && lastFence > firstLineEnd)
			{
				json = json[(firstLineEnd + 1)..lastFence].Trim();
			}
		}

		try
		{
			return JsonSerializer.Deserialize<BankStatement>(json, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			}) ?? throw new JsonException("The model returned an empty JSON value.");
		}
		catch (JsonException exception)
		{
			throw new SemanticKernelParsingException("invalid_model_json",
				$"Semantic Kernel returned invalid bank-statement JSON: {exception.Message}");
		}
	}
}
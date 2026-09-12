using System.Text;
using System.Text.Json;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace PdfReader.Api;

public sealed class LlamaSharpOptions
{
	public string? ModelPath { get; set; }
	public uint ContextSize { get; set; } = 4096;
	public int GpuLayerCount { get; set; }
	public int MaxTokens { get; set; } = 2048;
}

public sealed class LlamaSharpParsingException(string code, string message) : Exception(message)
{
	public string Code { get; } = code;
}

public sealed class LlamaSharpBankStatementParser : IDisposable
{
	private readonly LlamaSharpOptions options;
	private readonly object modelLock = new();
	private LLamaWeights? model;

	public LlamaSharpBankStatementParser(LlamaSharpOptions options)
	{
		this.options = options;
	}

	public async Task<BankStatement> ParseAsync(string extractedText, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(options.ModelPath))
		{
			throw new LlamaSharpParsingException("model_not_configured",
				"LlamaSharp is not configured. Set LlamaSharp:ModelPath to a local GGUF model file.");
		}

		if (!File.Exists(options.ModelPath))
		{
			throw new LlamaSharpParsingException("model_not_found",
				$"The configured LlamaSharp model was not found: {options.ModelPath}");
		}

		try
		{
			var prompt = BuildPrompt(extractedText);
			var response = new StringBuilder();
			var parameters = new InferenceParams
			{
				MaxTokens = options.MaxTokens,
				SamplingPipeline = new DefaultSamplingPipeline()
			};

			using var context = GetModel().CreateContext(new ModelParams(options.ModelPath!)
			{
				ContextSize = options.ContextSize,
				GpuLayerCount = options.GpuLayerCount
			});
			var session = new ChatSession(new InteractiveExecutor(context));

			await foreach (var token in session.ChatAsync(
				new ChatHistory.Message(AuthorRole.User, prompt), parameters).WithCancellation(cancellationToken))
			{
				response.Append(token);
			}

			return DeserializeResponse(response.ToString());
		}
		catch (LlamaSharpParsingException)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			throw new LlamaSharpParsingException("model_parse_failed",
				$"LlamaSharp could not parse the bank statement: {exception.Message}");
		}
	}

	private LLamaWeights GetModel()
	{
		lock (modelLock)
		{
			return model ??= LLamaWeights.LoadFromFile(new ModelParams(options.ModelPath!)
			{
				ContextSize = options.ContextSize,
				GpuLayerCount = options.GpuLayerCount
			});
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
			throw new LlamaSharpParsingException("invalid_model_json",
				$"LlamaSharp returned invalid bank-statement JSON: {exception.Message}");
		}
	}

	public void Dispose()
	{
		lock (modelLock)
		{
			model?.Dispose();
			model = null;
		}
	}
}
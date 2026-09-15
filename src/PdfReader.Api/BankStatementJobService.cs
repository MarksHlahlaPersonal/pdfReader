using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace PdfReader.Api;

public sealed record BankStatementJobResult(BankStatement? Statement, string? Error, string? Code)
{
	public bool Succeeded => Statement is not null;
}

public sealed class BankStatementJobService : BackgroundService
{
	private readonly Channel<BankStatementJob> jobs = Channel.CreateBounded<BankStatementJob>(100);
	private readonly ConcurrentDictionary<Guid, TaskCompletionSource<BankStatementJobResult>> results = new();
	private readonly PdfUploadService pdfUploadService;
	private readonly SemanticKernelBankStatementParser parser;

	public BankStatementJobService(
		PdfUploadService pdfUploadService,
		SemanticKernelBankStatementParser parser)
	{
		this.pdfUploadService = pdfUploadService;
		this.parser = parser;
	}

	public async Task<Guid> EnqueueAsync(byte[] pdfBytes, CancellationToken cancellationToken)
	{
		var id = Guid.NewGuid();
		results[id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
		await jobs.Writer.WriteAsync(new BankStatementJob(id, pdfBytes), cancellationToken);
		return id;
	}

	public bool TryGetResult(Guid id, out Task<BankStatementJobResult> result)
	{
		if (results.TryGetValue(id, out var completion))
		{
			result = completion.Task;
			return true;
		}

		result = Task.FromResult<BankStatementJobResult>(null!);
		return false;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var job in jobs.Reader.ReadAllAsync(stoppingToken))
		{
			if (!results.TryGetValue(job.Id, out var completion))
			{
				continue;
			}

			try
			{
				await using var uploadStream = new MemoryStream(job.PdfBytes, writable: false);
				var pdfResult = await pdfUploadService.ProcessAsync(uploadStream, stoppingToken);
				if (!pdfResult.IsPdf)
				{
					completion.TrySetResult(new(null, pdfResult.Error, "invalid_pdf"));
					continue;
				}

				if (pdfResult.RequiresOcr)
				{
					completion.TrySetResult(new(null,
						"The PDF does not contain readable text and requires OCR before it can be parsed.",
						"ocr_required"));
					continue;
				}

				var statement = await parser.ParseAsync(
					Encoding.UTF8.GetString(pdfResult.TextBytes), stoppingToken);
				completion.TrySetResult(new(statement, null, null));
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				completion.TrySetResult(new(null, "The request was cancelled while shutting down.", "cancelled"));
			}
			catch (SemanticKernelParsingException exception)
			{
				completion.TrySetResult(new(null, exception.Message, exception.Code));
			}
			catch (Exception exception)
			{
				completion.TrySetResult(new(null, exception.Message, "processing_failed"));
			}
		}
	}

	public bool TryRemove(Guid id) => results.TryRemove(id, out _);

	private sealed record BankStatementJob(Guid Id, byte[] PdfBytes);
}
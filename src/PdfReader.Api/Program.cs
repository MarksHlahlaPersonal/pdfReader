using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PdfReader.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PdfUploadService>();
builder.Services.AddSingleton<BankStatementParser>();
var semanticKernelOptions = builder.Configuration.GetSection("SemanticKernel").Get<SemanticKernelOptions>() ?? new();
builder.Services.AddSingleton(semanticKernelOptions);
builder.Services.AddHttpClient("SemanticKernel", client =>
{
	client.Timeout = TimeSpan.FromSeconds(semanticKernelOptions.HttpClientTimeoutSeconds);
});
builder.Services.AddSingleton<Kernel>(sp =>
{
	var kernelBuilder = Kernel.CreateBuilder();
	var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("SemanticKernel");
	kernelBuilder.AddOpenAIChatCompletion(
		modelId: semanticKernelOptions.Model,
		endpoint: new Uri(semanticKernelOptions.Endpoint),
		apiKey: semanticKernelOptions.ApiKey,
		orgId: null,
		serviceId: "llama.cpp",
		httpClient: httpClient);
	return kernelBuilder.Build();
});
builder.Services.AddSingleton<IChatCompletionService>(sp =>
	sp.GetRequiredService<Kernel>().GetRequiredService<IChatCompletionService>());
builder.Services.AddSingleton<SemanticKernelBankStatementParser>();
builder.Services.AddSingleton<BankStatementJobService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BankStatementJobService>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
	options.SwaggerDoc("v1", new() { Title = "PDF Reader API", Version = "v1" });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
	options.SwaggerEndpoint("/swagger/v1/swagger.json", "PDF Reader API v1");
	options.RoutePrefix = "swagger";
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/pdfs", async (IFormFile? file, PdfUploadService pdfUploadService,
	BankStatementParser bankStatementParser, CancellationToken cancellationToken) =>
{
	if (file is null || file.Length == 0)
	{
		return Results.BadRequest(new { error = "A non-empty PDF file is required." });
	}

	await using var uploadStream = file.OpenReadStream();
	var result = await pdfUploadService.ProcessAsync(uploadStream, cancellationToken);

	return result.IsPdf
		? Results.Ok(bankStatementParser.Parse(Encoding.UTF8.GetString(result.TextBytes)))
		: Results.BadRequest(result);
})
.DisableAntiforgery();

app.MapPost("/bank-statements", async (IFormFile? file, BankStatementJobService jobService,
	CancellationToken cancellationToken) =>
{
	if (file is null || file.Length == 0)
	{
		return Results.BadRequest(new { error = "A non-empty PDF bank statement is required.", code = "file_required" });
	}

	await using var uploadStream = file.OpenReadStream();
	await using var bufferedUpload = new MemoryStream();
	await uploadStream.CopyToAsync(bufferedUpload, cancellationToken);
	var requestId = await jobService.EnqueueAsync(bufferedUpload.ToArray(), cancellationToken);

	return Results.Accepted($"/bank-statements/{requestId}/events", new
	{
		requestId,
		eventsUrl = $"/bank-statements/{requestId}/events"
	});
})
.DisableAntiforgery();

app.MapGet("/bank-statements/{requestId:guid}/events", async (
	Guid requestId,
	BankStatementJobService jobService,
	HttpResponse response,
	CancellationToken cancellationToken) =>
{
	if (!jobService.TryGetResult(requestId, out var resultTask))
	{
		return Results.NotFound(new { error = "The bank-statement request was not found.", code = "request_not_found" });
	}

	response.ContentType = "text/event-stream";
	response.Headers.CacheControl = "no-cache";
	response.Headers.Connection = "keep-alive";

	await response.WriteAsync("event: processing\ndata: {\"status\":\"processing\"}\n\n", cancellationToken);
	await response.Body.FlushAsync(cancellationToken);

	while (!resultTask.IsCompleted)
	{
		var completedTask = await Task.WhenAny(resultTask, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
		if (completedTask != resultTask)
		{
			await response.WriteAsync(": keepalive\n\n", cancellationToken);
			await response.Body.FlushAsync(cancellationToken);
		}
	}

	var result = await resultTask;
	var eventName = result.Succeeded ? "completed" : "failed";
	var eventData = result.Succeeded
		? JsonSerializer.Serialize(new { status = "completed", statement = result.Statement })
		: JsonSerializer.Serialize(new { status = "failed", error = result.Error, code = result.Code });

	await response.WriteAsync($"event: {eventName}\ndata: {eventData}\n\n", cancellationToken);
	await response.Body.FlushAsync(cancellationToken);
	jobService.TryRemove(requestId);
	return Results.Empty;
})
.DisableAntiforgery();

app.Run();

public partial class Program;

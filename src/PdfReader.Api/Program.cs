using System.Text;
using PdfReader.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PdfUploadService>();
builder.Services.AddSingleton<BankStatementParser>();
builder.Services.AddSingleton(sp =>
	new LlamaSharpBankStatementParser(builder.Configuration.GetSection("LlamaSharp").Get<LlamaSharpOptions>() ?? new()));
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

<<<<<<< HEAD
app.MapPost("/bank-statements", async (IFormFile? file, PdfUploadService pdfUploadService,
	LlamaSharpBankStatementParser llamaParser, CancellationToken cancellationToken) =>
{
	if (file is null || file.Length == 0)
	{
		return Results.BadRequest(new { error = "A non-empty PDF file is required." });
	}


=======
app.MapPost("/bank-statements", async (HttpRequest request, PdfUploadService pdfUploadService,
	LlamaSharpBankStatementParser llamaParser, CancellationToken cancellationToken) =>
{
	IFormFile? file = null;
	if (request.HasFormContentType)
	{
		var form = await request.ReadFormAsync(cancellationToken);
		file = form.Files.GetFile("file");
	}

>>>>>>> e7c6623 (init commit)
	if (file is null || file.Length == 0)
	{
		return Results.BadRequest(new
		{
			error = "A non-empty PDF bank statement is required.",
			code = "file_required"
		});
	}

	await using var uploadStream = file.OpenReadStream();
	var pdfResult = await pdfUploadService.ProcessAsync(uploadStream, cancellationToken);
	if (!pdfResult.IsPdf)
	{
		return Results.BadRequest(new { error = pdfResult.Error, code = "invalid_pdf" });
	}

	if (pdfResult.RequiresOcr)
	{
		return Results.UnprocessableEntity(new
		{
			error = "The PDF does not contain readable text and requires OCR before it can be parsed.",
			code = "ocr_required"
		});
	}

	try
	{
		var statement = await llamaParser.ParseAsync(Encoding.UTF8.GetString(pdfResult.TextBytes), cancellationToken);
		return Results.Ok(statement);
	}
	catch (LlamaSharpParsingException exception)
	{
		return Results.UnprocessableEntity(new { error = exception.Message, code = exception.Code });
	}
})
.DisableAntiforgery();

app.Run();

public partial class Program;

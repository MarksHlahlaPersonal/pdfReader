# PDF Reader

This repository contains a .NET 10 minimal API managed by Aspire. The API accepts PDF uploads, validates the `%PDF-` file signature, extracts text with PdfPig, and returns the extracted text as UTF-8 bytes. A valid PDF that cannot produce readable text is reported with `requiresOcr: true`.

## Run with Aspire

```bash
aspire start --non-interactive
aspire ps
```

Use the API URL shown by `aspire ps` and upload a file as multipart form data:

```bash
curl -F "file=@document.pdf" http://localhost:<api-port>/pdfs
```

Stop the AppHost when finished:

```bash
aspire stop
```

## Test

```bash
dotnet test PdfReader.slnx
```

The service tests cover empty uploads, invalid signatures, unreadable PDFs that may need OCR, and readable PDFs whose extracted lines are stored as UTF-8 bytes.

## LlamaSharp bank-statement parsing

`POST /bank-statements` extracts text with PdfPig and sends that text to a local LlamaSharp GGUF model. Configure the model path before using this endpoint:

```json
{
	"LlamaSharp": {
		"ModelPath": "/models/your-bank-statement-model.gguf",
		"ContextSize": 4096,
		"GpuLayerCount": 0,
		"MaxTokens": 2048
	}
}
```

The endpoint returns `422` with a structured error when the model is not configured, cannot be loaded, or returns invalid JSON. It does not upload or download a model automatically.
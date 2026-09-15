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

## Semantic Kernel bank-statement parsing

`POST /bank-statements` queues the PDF and returns `202 Accepted` with a request ID. The PDF is processed in the background, and the result is streamed from `GET /bank-statements/{requestId}/events` using Server-Sent Events. The service sends `processing`, periodic keepalives, and either `completed` or `failed`.

The background parser sends extracted text through Semantic Kernel to a local llama.cpp server exposing the OpenAI-compatible API. Configure the endpoint and model name before using this endpoint:

```json
{
	"SemanticKernel": {
		"Endpoint": "http://localhost:8080/v1",
		"Model": "local-model",
		"ApiKey": "no-key",
		"MaxTokens": 2048,
		"HttpClientTimeoutSeconds": 900
	}
}
```

The endpoint returns `422` with a structured error when the endpoint or model is not configured, the local server cannot be reached, or the model returns invalid JSON. It does not upload or download a model automatically.
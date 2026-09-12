#:sdk Aspire.AppHost.Sdk@13.5.3
#:property AspireUseCliBundle=true
#:project src/PdfReader.Api/PdfReader.Api.csproj

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.PdfReader_Api>("api")
	.WithHttpEndpoint(port: 5000, name: "http");

builder.Build().Run();
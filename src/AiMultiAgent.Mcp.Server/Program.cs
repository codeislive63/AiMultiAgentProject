using AiMultiAgent.Core.Agents.CodeReview;
using AiMultiAgent.Core.Agents.Documentation;
using AiMultiAgent.Core.Agents.Pm;
using AiMultiAgent.Core.Agents.Pm.Llm;
using AiMultiAgent.Mcp.Client;
using GenerativeAI;
using GenerativeAI.Microsoft;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Scalar.AspNetCore;
using static System.Net.WebRequestMethods;

const string McpPath = "/api/mcp";


var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ContractResolver = new DefaultContractResolver();
        options.SerializerSettings.Formatting = Formatting.Indented;
    });


builder.Services.AddMemoryCache();

builder.Services.AddOpenApi();


builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = false;
});

var baseUri = builder.Configuration["API_URI"]
              ?? Environment.GetEnvironmentVariable("API_URI")
              ?? throw new InvalidOperationException("API_URI is not set");


builder.Services.AddSseMcpClient(
    options => options.EndpointPath = McpPath,
     http =>
     {
         http.BaseAddress = new Uri(baseUri);
         http.Timeout = Timeout.InfiniteTimeSpan; // ждать столько, сколько сервер отдаёт
                                                  // или: http.Timeout = TimeSpan.FromMinutes(10); // ждать максимум 10 минут
     }
);


builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();


var geminiApiKey = builder.Configuration["GEMINI_API_KEY"] ??
                   Environment.GetEnvironmentVariable("GEMINI_API_KEY");

Console.WriteLine(geminiApiKey);

if (string.IsNullOrWhiteSpace(geminiApiKey))
{
    throw new InvalidOperationException(
        "GEMINI_API_KEY is not set. Configure it in user-secrets or environment variables."
    );
}

builder.Services.AddSingleton<IChatClient>(_ =>
{
    return new GenerativeAIChatClient(
        apiKey: geminiApiKey,
        modelName: GoogleAIModels.DefaultGeminiModel
    );
});

// Агенты
builder.Services.AddSingleton<PmAgent>();
builder.Services.AddSingleton<IPmPlanner, GeminiPmLlmPlanner>();

builder.Services.AddSingleton<CodeReviewerAgent>();
builder.Services.AddSingleton<DocumentationAgent>();


var app = builder.Build();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.MapScalarApiReference(options =>
    {
        options.Title = "AiMultiAgentProject MCP Debugger";
        options.Theme = ScalarTheme.Saturn;
        options.Layout = ScalarLayout.Modern;
    });

    app.MapGet("/", () => Results.Redirect("/scalar"));
}

app.MapControllers();

app.MapMcp(McpPath);

app.Run();

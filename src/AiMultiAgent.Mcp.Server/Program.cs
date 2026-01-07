using AiMultiAgent.Core.Agents.CodeReview;
using AiMultiAgent.Core.Agents.Documentation;
using AiMultiAgent.Core.Agents.Pm;
using AiMultiAgent.Mcp.Client;
using Newtonsoft.Json;
using Scalar.AspNetCore;


const string McpPath = "/api/mcp";


var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.Formatting = Formatting.Indented;
    });

builder.Services.AddOpenApi();


builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = false;
});


builder.Services.AddSseMcpClient(
    options => options.EndpointPath = McpPath,
    http => http.BaseAddress = new Uri("https://localhost:7244")
);


builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();


// Агенты
builder.Services.AddSingleton<PmAgent>();
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

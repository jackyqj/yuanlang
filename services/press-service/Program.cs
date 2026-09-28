using Press.Adapters.Abstractions;
using Press.Adapters.Mes;
using Press.Adapters.Simulation;
using Press.Judge;
using Press.Service;
using Press.Service.Application;
using Press.Service.Http;
using Press.Service.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://127.0.0.1:5080");

var dataRoot = builder.Configuration["Data:Root"]
               ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "data"));
if (!Directory.Exists(dataRoot))
{
    var alt = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
    if (Directory.Exists(alt)) dataRoot = alt;
}
Directory.CreateDirectory(dataRoot);

builder.Services.AddSingleton<SimulatedPressHardware>();
builder.Services.AddSingleton<IJudgeEngine, SimpleJudgeEngine>();
builder.Services.AddSingleton<IPressStore, SqlitePressStore>();
builder.Services.AddSingleton<IMesOutbox, SqliteMesOutbox>();
builder.Services.AddSingleton<IMesPublisher>(_ =>
{
    var adapter = builder.Configuration["Mes:Adapter"] ?? "file";
    return adapter.Equals("fail", StringComparison.OrdinalIgnoreCase)
        ? new FailingMesPublisher()
        : new FileMesPublisher(Path.Combine(dataRoot, "mes", "acked"));
});
builder.Services.AddSingleton<MachineRuntime>();
builder.Services.AddHostedService<StoreBootstrapper>();
builder.Services.AddHostedService<MesOutboxBootstrapper>();
builder.Services.AddHostedService<RuntimeBootstrapper>();
builder.Services.AddHostedService<MesOutboxWorker>();
builder.Services.AddHostedService<DemoCycleHostedService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials()
            .SetIsOriginAllowed(origin =>
                origin.StartsWith("http://127.0.0.1:") ||
                origin.StartsWith("http://localhost:")));
});

var app = builder.Build();
app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "ok", mode = "simulation" }));
app.MapRuntimeHttp();
app.MapTraceHttp();
app.MapRecipeHttp();
app.MapMesHttp();
app.MapSpcHttp();
app.Run();

public partial class Program;

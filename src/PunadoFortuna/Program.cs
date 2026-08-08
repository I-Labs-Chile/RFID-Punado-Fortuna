using PunadoFortuna.Hubs;
using PunadoFortuna.Models;
using PunadoFortuna.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<SessionLogger>();

var chipMappingsPath = FindDataFile("mapeo-fichas.json");

Console.WriteLine($"Mapeo: {chipMappingsPath}");

List<ChipMapping> chipMappings;

if (File.Exists(chipMappingsPath))
{
    var json = File.ReadAllText(chipMappingsPath);
    chipMappings = JsonSerializer.Deserialize<List<ChipMapping>>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? [];
}
else
{
    chipMappings = GenerateDefaultMappings();
    Console.WriteLine("mapeo-fichas.json no encontrado, usando mapeo por defecto");
}

builder.Services.AddSingleton(chipMappings);
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddSingleton<GameOrchestrator>();

var useSimulation = !args.Contains("--no-sim");
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<RfidReaderService>>();
    var sessionLogger = sp.GetRequiredService<SessionLogger>();
    var mappings = sp.GetRequiredService<List<ChipMapping>>();
    return new RfidReaderService(logger, sessionLogger, mappings, useSimulation);
});

builder.Services.AddHostedService<RfidBackgroundService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<GameHub>("/gamehub");

var orchestrator = app.Services.GetRequiredService<GameOrchestrator>();
orchestrator.Start(app.Services.GetRequiredService<IHubContext<GameHub>>());

var sessionLogger = app.Services.GetRequiredService<SessionLogger>();
sessionLogger.StartSession();

app.Lifetime.ApplicationStopping.Register(() =>
{
    sessionLogger.StopSession();
});

app.Run();

static string FindDataFile(string filename)
{
    var candidates = new[]
    {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", filename),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "data", filename),
        Path.Combine(Directory.GetCurrentDirectory(), "data", filename)
    };

    foreach (var path in candidates)
    {
        if (File.Exists(path)) return path;
    }

    return candidates[0];
}

static List<ChipMapping> GenerateDefaultMappings()
{
    var mappings = new List<ChipMapping>();
    var epcsZona1 = new[] {
        "300833B2DDD9014000000000", "300833B2DDD9014000000001", "300833B2DDD9014000000002",
        "300833B2DDD9014000000003", "300833B2DDD9014000000004", "300833B2DDD9014000000005",
        "300833B2DDD9014000000006", "300833B2DDD9014000000007", "300833B2DDD9014000000008",
        "300833B2DDD9014000000009"
    };
    var epcsZona2 = new[] {
        "300833B2DDD9014000000010", "300833B2DDD9014000000011", "300833B2DDD9014000000012",
        "300833B2DDD9014000000013", "300833B2DDD9014000000014", "300833B2DDD9014000000015",
        "300833B2DDD9014000000016", "300833B2DDD9014000000017", "300833B2DDD9014000000018",
        "300833B2DDD9014000000019"
    };

    var rng = new Random(42);
    for (int i = 0; i < epcsZona1.Length; i++)
        mappings.Add(new ChipMapping { Epc = epcsZona1[i], ZonaId = 1, Valor = rng.Next(1, 11), Descripcion = $"Ficha Z1 #{i + 1}" });
    for (int i = 0; i < epcsZona2.Length; i++)
        mappings.Add(new ChipMapping { Epc = epcsZona2[i], ZonaId = 2, Valor = rng.Next(1, 11), Descripcion = $"Ficha Z2 #{i + 1}" });

    return mappings;
}

public class GameOrchestrator
{
    private readonly RfidReaderService _reader;
    private readonly GameEngine _engine;
    private readonly SessionLogger _sessionLogger;
    private readonly ILogger<GameOrchestrator> _logger;
    private IHubContext<GameHub>? _hub;

    public GameOrchestrator(
        RfidReaderService reader,
        GameEngine engine,
        SessionLogger sessionLogger,
        ILogger<GameOrchestrator> logger)
    {
        _reader = reader;
        _engine = engine;
        _sessionLogger = sessionLogger;
        _logger = logger;
    }

    public void Start(IHubContext<GameHub> hub)
    {
        _hub = hub;

        _reader.TagsRead += (_, e) =>
        {
            _engine.ProcessTagCycle(e.Tags);
        };

        _engine.StateChanged += async (_, state) =>
        {
            _sessionLogger.LogGameState(state);
            if (_hub != null)
                await _hub.Clients.All.SendAsync("GameStateUpdate", state);
        };

        _reader.ConnectionChanged += async (_, e) =>
        {
            if (_hub != null)
                await _hub.Clients.All.SendAsync("ConnectionChanged", e.Connected);
        };
    }
}

public class RfidBackgroundService : BackgroundService
{
    private readonly RfidReaderService _readerService;
    private readonly ILogger<RfidBackgroundService> _logger;

    public RfidBackgroundService(RfidReaderService readerService, ILogger<RfidBackgroundService> logger)
    {
        _readerService = readerService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Conectando al reader...");
        await _readerService.ConnectAsync("192.168.1.100", 5084);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        await _readerService.DisconnectAsync();
    }
}

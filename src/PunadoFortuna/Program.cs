using Microsoft.AspNetCore.SignalR;
using PunadoFortuna.Hubs;
using PunadoFortuna.Models;
using PunadoFortuna.Services;
using System.Text.Json;

// --discover: modo diagnóstico (solo descubrimiento, sin levantar servidor web)
if (args.Contains("discover"))
{
    await RunDiscovery(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<SessionLogger>();
builder.Services.AddSingleton<DeviceDiscoveryService>();

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

// --- Fx9600 Discovery ---
var fx9600Config = builder.Configuration.GetSection("Fx9600");
var staticIp = fx9600Config["IpAddress"] ?? "auto";
var port = int.TryParse(fx9600Config["Port"], out var p) ? p : 5084;

Console.WriteLine("==========================================");
Console.WriteLine("  Puñado de Fortuna - Iniciando...");
Console.WriteLine("==========================================");

DeviceDiscoveryResult discoveryResult;

if (staticIp.Equals("auto", StringComparison.OrdinalIgnoreCase))
{
    var discoveryLogger = builder.Services.BuildServiceProvider()
        .GetRequiredService<ILogger<DeviceDiscoveryService>>();

    var tcpTimeout = int.TryParse(fx9600Config["TcpTimeoutMs"], out var tcp) ? tcp : 2000;
    var pingTimeout = int.TryParse(fx9600Config["PingTimeoutMs"], out var ping) ? ping : 500;
    var maxConcurrency = int.TryParse(fx9600Config["MaxPingConcurrency"], out var mc) ? mc : 50;

    var discoveryService = new DeviceDiscoveryService(
        discoveryLogger,
        TimeSpan.FromMilliseconds(pingTimeout),
        TimeSpan.FromMilliseconds(tcpTimeout),
        maxConcurrency);

    discoveryResult = await discoveryService.DiscoverAsync(null, port);

    if (discoveryResult.IpAddress != null)
    {
        Console.WriteLine($"> FX9600 encontrado en {discoveryResult.IpAddress}:{discoveryResult.Port}");
        Console.WriteLine($"> Método: {discoveryResult.DiscoveryMethod}");

        PersistDiscoveredIp(
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            discoveryResult.IpAddress,
            port);
    }
    else
    {
        Console.WriteLine("> FX9600 NO encontrado. Se seguirá buscando en background.");
    }
}
else
{
    discoveryResult = DeviceDiscoveryResult.FromConfig(staticIp, port);
    Console.WriteLine($"> Usando IP fija: {staticIp}:{port}");
}

// Imprimir diagnóstico
foreach (var diag in discoveryResult.Diagnostics)
{
    Console.WriteLine($"  [DIAG] {diag}");
}

Console.WriteLine("==========================================");

builder.Services.AddSingleton(discoveryResult);

var useSimulation = args.Contains("--no-sim");
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

static async Task RunDiscovery(string[] args)
{
    var configBuilder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .Build();

    var fxConfig = configBuilder.GetSection("Fx9600");
    var port = int.TryParse(fxConfig["Port"], out var p) ? p : 5084;
    var tcpTimeout = int.TryParse(fxConfig["TcpTimeoutMs"], out var tcp) ? tcp : 2000;
    var pingTimeout = int.TryParse(fxConfig["PingTimeoutMs"], out var ping) ? ping : 500;
    var maxConcurrency = int.TryParse(fxConfig["MaxPingConcurrency"], out var mc) ? mc : 50;

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
    var logger = loggerFactory.CreateLogger<DeviceDiscoveryService>();
    var service = new DeviceDiscoveryService(
        logger,
        TimeSpan.FromMilliseconds(pingTimeout),
        TimeSpan.FromMilliseconds(tcpTimeout),
        maxConcurrency);

    var staticIpOverride = args.Length > 1 ? args[1]
        : fxConfig["IpAddress"]?.Equals("auto", StringComparison.OrdinalIgnoreCase) == true ? null
        : fxConfig["IpAddress"];

    Console.WriteLine();
    Console.WriteLine("==========================================");
    Console.WriteLine("  MODO DESCUBRIMIENTO - FX9600");
    Console.WriteLine("==========================================");
    Console.WriteLine();

    var result = await service.DiscoverAsync(staticIpOverride, port);

    if (result.IpAddress != null)
    {
        PersistDiscoveredIp(
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            result.IpAddress,
            port);
    }

    Console.WriteLine();
    Console.WriteLine("--- RESULTADO ---");
    Console.WriteLine($"IP:          {(result.IpAddress ?? "NO ENCONTRADO")}");
    Console.WriteLine($"Puerto LLRP: {result.Port}");
    Console.WriteLine($"Método:      {result.DiscoveryMethod}");
    Console.WriteLine($"LLRP (5084): {(result.LLRPReachable ? "OK" : "FALLÓ")}");
    Console.WriteLine($"HTTP (80):   {(result.HttpReachable ? "OK" : "FALLÓ")}");
    Console.WriteLine();
    Console.WriteLine("--- DIAGNÓSTICO ---");
    foreach (var diag in result.Diagnostics)
    {
        Console.WriteLine($"  {diag}");
    }

    Console.WriteLine();
    Console.WriteLine("==========================================");
}

static void PersistDiscoveredIp(string configPath, string ip, int port)
{
    try
    {
        if (!File.Exists(configPath)) return;
        var json = File.ReadAllText(configPath);

        var updated = System.Text.RegularExpressions.Regex.Replace(
            json,
            @"""IpAddress""\s*:\s*""[^""]*""",
            $"\"IpAddress\": \"{ip}\"");

        if (updated != json)
        {
            File.WriteAllText(configPath, updated);
            Console.WriteLine($"> IP guardada en appsettings.json: {ip}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"> No se pudo guardar la IP automáticamente: {ex.Message}");
    }
}

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
    private readonly DeviceDiscoveryResult _discoveryResult;
    private readonly DeviceDiscoveryService _discoveryService;
    private readonly ILogger<RfidBackgroundService> _logger;

    public RfidBackgroundService(
        RfidReaderService readerService,
        DeviceDiscoveryResult discoveryResult,
        DeviceDiscoveryService discoveryService,
        ILogger<RfidBackgroundService> logger)
    {
        _readerService = readerService;
        _discoveryResult = discoveryResult;
        _discoveryService = discoveryService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var host = _discoveryResult.IpAddress;
        var port = _discoveryResult.Port;

        if (host == null)
        {
            _logger.LogWarning("FX9600 no encontrado al inicio. Entrando en modo búsqueda continua...");

            while (!stoppingToken.IsCancellationRequested && host == null)
            {
                _logger.LogInformation("Reintentando descubrimiento del FX9600...");
                var result = await _discoveryService.DiscoverAsync(null, port, stoppingToken);
                host = result.IpAddress;

                if (host == null)
                {
                    _logger.LogInformation("FX9600 aún no encontrado. Reintentando en 10s...");
                    await Task.Delay(10000, stoppingToken);
                }
            }
        }

        if (host != null)
        {
            _logger.LogInformation("Conectando al reader en {Host}:{Port}...", host, port);
            await _readerService.ConnectAsync(host, port);
        }
        else
        {
            _logger.LogError("No se pudo encontrar el FX9600 después de múltiples intentos");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        await _readerService.DisconnectAsync();
    }
}

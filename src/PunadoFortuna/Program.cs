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

// --- Cargar mapeo de colores ---
var colorMapPath = FindDataFile("mapeo-colores.json");
Console.WriteLine($"Mapeo colores: {colorMapPath}");

Dictionary<string, string> colorMap;
if (File.Exists(colorMapPath))
{
    var json = File.ReadAllText(colorMapPath);
    var mappings = JsonSerializer.Deserialize<List<ColorMapping>>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? [];
    colorMap = mappings.ToDictionary(m => m.Epc, m => m.Color);
    Console.WriteLine($"Colores cargados: {colorMap.Count}");
}
else
{
    colorMap = new Dictionary<string, string>();
    Console.WriteLine("mapeo-colores.json no encontrado");
}

builder.Services.AddSingleton(colorMap);

// --- Cargar mapeo de fichas (para total conocido) ---
var chipMappingsPath = FindDataFile("mapeo-fichas.json");
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
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<GameEngine>>();
    var sessionLogger = sp.GetRequiredService<SessionLogger>();
    var maps = sp.GetRequiredService<List<ChipMapping>>();
    var colors = sp.GetRequiredService<Dictionary<string, string>>();
    return new GameEngine(logger, sessionLogger, maps, colors);
});

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

foreach (var diag in discoveryResult.Diagnostics)
    Console.WriteLine($"  [DIAG] {diag}");

Console.WriteLine("==========================================");

builder.Services.AddSingleton(discoveryResult);

var useSimulation = !args.Contains("--no-sim");

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<RfidReaderService>>();
    var sessionLogger = sp.GetRequiredService<SessionLogger>();
    var mappings = sp.GetRequiredService<List<ChipMapping>>();
    return new RfidReaderService(logger, sessionLogger, mappings, useSimulation);
});

builder.Services.AddSingleton<GameOrchestrator>();
builder.Services.AddHostedService<RfidBackgroundService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<GameHub>("/gamehub");

var orchestrator = app.Services.GetRequiredService<GameOrchestrator>();
orchestrator.Start(app.Services.GetRequiredService<IHubContext<GameHub>>());

var sessionLogger = app.Services.GetRequiredService<SessionLogger>();
sessionLogger.StartSession();

app.Lifetime.ApplicationStopping.Register(() => sessionLogger.StopSession());

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
        PersistDiscoveredIp(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), result.IpAddress, port);

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
        Console.WriteLine($"  {diag}");
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
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "data", filename),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "data", filename),
        Path.Combine(Directory.GetCurrentDirectory(), "data", filename)
    };

    foreach (var path in candidates)
        if (File.Exists(path)) return path;

    return candidates[0];
}

static List<ChipMapping> GenerateDefaultMappings()
{
    return new List<ChipMapping>
    {
        new() { Epc = "E28069150000501A009264E2", ZonaId = 1, Valor = 1, Descripcion = "Ficha 1" },
        new() { Epc = "E28069150000401A009264E3", ZonaId = 1, Valor = 1, Descripcion = "Ficha 2" },
        new() { Epc = "E28069150000401A009264DF", ZonaId = 1, Valor = 1, Descripcion = "Ficha 3" },
        new() { Epc = "E28069150000401A009264E0", ZonaId = 1, Valor = 1, Descripcion = "Ficha 4" },
        new() { Epc = "E28069150000501A009264E1", ZonaId = 1, Valor = 1, Descripcion = "Ficha 5" }
    };
}

public class ColorMapping
{
    public string Epc { get; set; } = "";
    public string Color { get; set; } = "";
    public string Descripcion { get; set; } = "";
}

public class GameOrchestrator
{
    private readonly RfidReaderService _reader;
    private readonly GameEngine _engine;
    private readonly SessionLogger _sessionLogger;
    private IHubContext<GameHub>? _hub;

    public GameOrchestrator(
        RfidReaderService reader,
        GameEngine engine,
        SessionLogger sessionLogger)
    {
        _reader = reader;
        _engine = engine;
        _sessionLogger = sessionLogger;
    }

    public void Start(IHubContext<GameHub> hub)
    {
        _hub = hub;

        _reader.TagsRead += (_, e) => _engine.ProcessTags(e.Tags);

        _engine.StateChanged += async (_, state) =>
        {
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
        _logger.LogInformation("=== Protocolo de conexión iniciado ===");

        // Esperar que el puerto se libere (por si 123RFID estaba corriendo)
        _logger.LogInformation("Esperando 3s para estabilización del puerto...");
        await Task.Delay(3000, stoppingToken);

        var host = _discoveryResult.IpAddress;
        var port = _discoveryResult.Port;

        if (host == null)
        {
            _logger.LogInformation("IP no configurada. Ejecutando descubrimiento...");
            host = await DiscoverWithRetry(port, stoppingToken);
        }
        else
        {
            _logger.LogInformation("IP configurada: {Host}:{Port}. Verificando...", host, port);
            var alive = await ProbeReaderAsync(host, port);
            if (!alive)
            {
                _logger.LogWarning("IP configurada no responde. Ejecutando descubrimiento...");
                host = await DiscoverWithRetry(port, stoppingToken);
            }
        }

        if (host == null)
        {
            _logger.LogError("No se pudo encontrar el FX9600. Modo offline.");
            while (!stoppingToken.IsCancellationRequested)
                await Task.Delay(5000, stoppingToken);
            return;
        }

        _logger.LogInformation("FX9600 confirmado en {Host}:{Port}. Conectando SDK...", host, port);

        // Conexión con reintentos
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await _readerService.ConnectAsync(host, port);
                _logger.LogInformation("Conexión establecida y leyendo");
                break;
            }
            catch (Exception ex) when (attempt < 5)
            {
                _logger.LogWarning(ex, "Intento {Attempt}/5 fallido. Reintentando en {Delay}s...",
                    attempt, attempt * 2);
                await Task.Delay(attempt * 2000, stoppingToken);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(1000, stoppingToken);

        await _readerService.DisconnectAsync();
    }

    private async Task<string?> DiscoverWithRetry(int port, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            var result = await _discoveryService.DiscoverAsync(null, port, ct);
            if (result.IpAddress != null)
            {
                _logger.LogInformation("FX9600 descubierto: {Ip} ({Method})",
                    result.IpAddress, result.DiscoveryMethod);
                return result.IpAddress;
            }

            _logger.LogInformation("Intento {Attempt}/10 sin resultado. Reintentando en 5s...", attempt);
            await Task.Delay(5000, ct);
        }
        return null;
    }

    private static async Task<bool> ProbeReaderAsync(string host, int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(3));
            return true;
        }
        catch
        {
            return false;
        }
    }
}

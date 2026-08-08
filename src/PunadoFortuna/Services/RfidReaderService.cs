using PunadoFortuna.Models;

namespace PunadoFortuna.Services;

public class RfidReaderEventArgs : EventArgs
{
    public List<TagRead> Tags { get; set; } = new();
}

public class RfidConnectionEventArgs : EventArgs
{
    public bool Connected { get; set; }
    public string? Error { get; set; }
}

public class RfidReaderService : IDisposable
{
    private readonly ILogger<RfidReaderService> _logger;
    private readonly SessionLogger _sessionLogger;
    private readonly List<ChipMapping> _chipMappings;
    private Timer? _simulationTimer;
    private readonly Random _rng = new();
    private int _cycleCount;

    public bool IsConnected { get; private set; }
    public bool IsSimulationMode { get; }

    public event EventHandler<RfidReaderEventArgs>? TagsRead;
    public event EventHandler<RfidConnectionEventArgs>? ConnectionChanged;

    public RfidReaderService(
        ILogger<RfidReaderService> logger,
        SessionLogger sessionLogger,
        List<ChipMapping> chipMappings,
        bool simulationMode = true)
    {
        _logger = logger;
        _sessionLogger = sessionLogger;
        _chipMappings = chipMappings;
        IsSimulationMode = simulationMode;
    }

    public async Task ConnectAsync(string host, int port = 5084, int timeoutMs = 5000)
    {
        if (IsSimulationMode)
        {
            _logger.LogInformation("Modo simulación: conectado virtualmente a reader {Host}:{Port}", host, port);
            IsConnected = true;
            ConnectionChanged?.Invoke(this, new RfidConnectionEventArgs { Connected = true });
            _sessionLogger.LogRaw("CONNECTION", $"Simulated connect to {host}:{port}");

            _simulationTimer = new Timer(SimulateInventoryCycle, null, 0, 100);
            return;
        }

        // TODO: Implementar con SDK Zebra real
        // var reader = new RFIDReader(host, port, timeoutMs);
        // reader.Connect();
        // reader.Events.EventReadNotify += OnTagReadEvent;
        // reader.Events.EventStatusNotify += OnStatusEvent;
        // reader.Actions.Inventory.Perform();
        // IsConnected = true;

        await Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        if (_simulationTimer != null)
        {
            await _simulationTimer.DisposeAsync();
            _simulationTimer = null;
        }

        IsConnected = false;
        ConnectionChanged?.Invoke(this, new RfidConnectionEventArgs { Connected = false });
        _sessionLogger.LogRaw("CONNECTION", "Disconnected");
        _logger.LogInformation("Desconectado del reader");
    }

    public async Task ReconnectAsync(string host, int port = 5084)
    {
        _logger.LogInformation("Intentando reconexión...");
        _sessionLogger.LogRaw("RECONNECT", $"Attempting reconnect to {host}:{port}");

        for (int attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                _logger.LogInformation("Intento de reconexión {Attempt}/10", attempt);
                await ConnectAsync(host, port);
                if (IsConnected)
                {
                    _logger.LogInformation("Reconexión exitosa en intento {Attempt}", attempt);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconexión fallida intento {Attempt}", attempt);
            }

            int delayMs = (int)(1000 * Math.Pow(2, attempt));
            _logger.LogInformation("Esperando {Delay}ms antes del próximo intento", delayMs);
            await Task.Delay(delayMs);
        }

        _logger.LogError("Reconexión fallida después de 10 intentos");
        _sessionLogger.LogRaw("RECONNECT_FAILED", "All reconnect attempts exhausted");
    }

    public async Task ResetReaderAsync()
    {
        await DisconnectAsync();
        _logger.LogInformation("Reader reseteado");
    }

    private void SimulateInventoryCycle(object? state)
    {
        _cycleCount++;
        var tags = new List<TagRead>();
        var now = DateTimeOffset.UtcNow;

        var zonaIds = _chipMappings.Select(c => c.ZonaId).Distinct();
        foreach (var zonaId in zonaIds)
        {
            var chips = _chipMappings.Where(c => c.ZonaId == zonaId).ToList();
            short antennaId = (short)zonaId;

            foreach (var chip in chips)
            {
                if (_rng.NextDouble() > 0.03)
                {
                    tags.Add(new TagRead
                    {
                        Epc = chip.Epc,
                        AntennaId = antennaId,
                        PeakRssi = (short)_rng.Next(-60, -20),
                        SeenCount = _rng.Next(1, 999),
                        Phase = (short)_rng.Next(0, 360),
                        ChannelIndex = (short)_rng.Next(1, 50),
                        Timestamp = now
                    });
                }
            }
        }

        _sessionLogger.LogTagReads(tags);

        TagsRead?.Invoke(this, new RfidReaderEventArgs { Tags = tags });
    }

    public void Dispose()
    {
        _simulationTimer?.Dispose();
    }
}

using PunadoFortuna.Models;
using Symbol.RFID3;

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
    private RFIDReader? _reader;
    private Timer? _simulationTimer;
    private readonly Random _rng = new();
    private volatile bool _stopPolling;

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

        await Task.Run(() =>
        {
            _logger.LogInformation("SDK: Conectando a {Host}:{Port}...", host, port);

            _reader = new RFIDReader(host, (uint)port, (uint)timeoutMs);
            _reader.Connect();

            _reader.Events.ReadNotify += OnReadNotify;

            _reader.Actions.Inventory.Perform();

            _sessionLogger.LogRaw("CONNECTION", $"Connected to {host}:{port} via SDK");
            _sessionLogger.LogRaw("LLRP_STARTED", $"Inventory started on {host}:{port}");

            IsConnected = true;
            ConnectionChanged?.Invoke(this, new RfidConnectionEventArgs { Connected = true });

            _logger.LogInformation("SDK: Conectado y leyendo");
        });
    }

    public async Task DisconnectAsync()
    {
        if (_simulationTimer != null)
        {
            await _simulationTimer.DisposeAsync();
            _simulationTimer = null;
        }

        if (_reader != null && IsConnected && !IsSimulationMode)
        {
            await Task.Run(() =>
            {
                try
                {
                    _stopPolling = true;
                    _reader.Actions.Inventory.Stop();
                    _reader.Events.ReadNotify -= OnReadNotify;
                    _reader.Disconnect();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error al desconectar reader");
                }
                finally
                {
                    _reader.Dispose();
                    _reader = null;
                }
            });
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

    private void OnReadNotify(object? sender, Events.ReadEventArgs e)
    {
        if (_reader == null || _stopPolling) return;

        try
        {
            var rawTags = _reader.Actions.GetReadTags(100);
            if (rawTags == null || rawTags.Length == 0) return;

            var tags = new List<TagRead>();

            foreach (var raw in rawTags)
            {
                // Solo tags de inventario (sin operación de acceso)
                if (raw.OpCode != ACCESS_OPERATION_CODE.ACCESS_OPERATION_NONE &&
                    raw.OpCode != ACCESS_OPERATION_CODE.ACCESS_OPERATION_READ)
                    continue;

                var tag = new TagRead
                {
                    Epc = raw.TagID ?? "",
                    AntennaId = (short)raw.AntennaID,
                    PeakRssi = raw.PeakRSSI,
                    SeenCount = (int)raw.TagSeenCount,
                    Phase = 0, // SDK v1.2 no expone Phase directamente
                    ChannelIndex = (short)raw.ChannelIndex,
                    Timestamp = DateTimeOffset.UtcNow
                };

                tags.Add(tag);

                _logger.LogDebug(
                    "TAG: {Epc} ANT:{Ant} RSSI:{Rssi} CNT:{Cnt} PHASE:{Phase} CH:{Ch}",
                    tag.Epc, tag.AntennaId, tag.PeakRssi, tag.SeenCount, tag.Phase, tag.ChannelIndex);
            }

            if (tags.Count > 0)
            {
                _sessionLogger.LogTagReads(tags);
                TagsRead?.Invoke(this, new RfidReaderEventArgs { Tags = tags });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando tags del SDK");
        }
    }

    private void SimulateInventoryCycle(object? state)
    {
        Interlocked.Increment(ref _cycleCount);
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

    private int _cycleCount;

    public void Dispose()
    {
        _simulationTimer?.Dispose();
        _reader?.Dispose();
    }
}

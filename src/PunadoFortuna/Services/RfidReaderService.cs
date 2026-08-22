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
            _reader.Config.RadioPowerState = RADIO_POWER_STATE.ON;

            try
            {
                LogReaderCapabilities();

                var availableAntennas = _reader.Config.Antennas.AvailableAntennas;
                var connectedAntennas = new List<int>();

                foreach (var antId in availableAntennas)
                {
                    try
                    {
                        var physicalProps = _reader.Config.Antennas[antId].GetPhysicalProperties();
                        if (physicalProps.IsConnected)
                        {
                            connectedAntennas.Add(antId);
                            _logger.LogInformation("Antena {AntennaId}: CONECTADA (gain={Gain}dB)", antId, physicalProps.AntennaGain);
                        }
                        else
                        {
                            _logger.LogInformation("Antena {AntennaId}: desconectada", antId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Antena {AntennaId}: no se pudo verificar estado", antId);
                    }
                }

                if (connectedAntennas.Count == 0)
                {
                    _logger.LogWarning("No se detectaron antenas conectadas. Configurando todas las disponibles por defecto.");
                    connectedAntennas = availableAntennas.Select(a => (int)a).ToList();
                }

                _logger.LogInformation("Configurando {Count} antena(s) conectada(s): [{Ids}]",
                    connectedAntennas.Count, string.Join(", ", connectedAntennas));

                foreach (var antId in connectedAntennas)
                    ConfigureAntenna((ushort)antId);

                _reader.Config.SaveLlrpConfig(IntPtr.Zero);
                _logger.LogInformation("Configuración LLRP persistida en reader (SaveLlrpConfig)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Config RF parcial. Usando defaults.");
            }

            _reader.Events.ReadNotify += OnReadNotify;
            _reader.Events.AttachTagDataWithReadEvent = false;
            _reader.Events.StatusNotify += OnStatusNotify;
            _reader.Events.NotifyInventoryStartEvent = true;
            _reader.Events.NotifyInventoryStopEvent = true;
            _reader.Events.NotifyAccessStartEvent = true;
            _reader.Events.NotifyAccessStopEvent = true;
            _reader.Events.NotifyReaderDisconnectEvent = true;
            _reader.Events.NotifyReaderExceptionEvent = true;
            _reader.Events.NotifyBufferFullEvent = true;
            _reader.Events.NotifyBufferFullWarningEvent = true;
            _reader.Events.NotifyAntennaEvent = true;
            _reader.Events.NotifyGPIEvent = true;
            _reader.Events.NotifyTemperatureAlarmEvent = true;

            _logger.LogInformation("SDK: Eventos registrados. Iniciando Inventory.Perform()...");
            _reader.Actions.Inventory.Perform();
            _logger.LogInformation("SDK: Inventory.Perform() ejecutado OK");

            _sessionLogger.LogRaw("CONNECTION", $"Connected to {host}:{port} via SDK");
            _sessionLogger.LogRaw("LLRP_STARTED", $"Inventory started on {host}:{port}");

            IsConnected = true;
            ConnectionChanged?.Invoke(this, new RfidConnectionEventArgs { Connected = true });

            _logger.LogInformation("SDK: Conectado y leyendo (antenas configuradas, S1, Pop=32, DenseReader, persistido)");
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

    private void LogReaderCapabilities()
    {
        if (_reader == null) return;

        var caps = _reader.ReaderCapabilities;

        var availableAntennas = _reader.Config.Antennas.AvailableAntennas;
        _logger.LogInformation("=== CAPACIDADES DEL READER ===");
        _logger.LogInformation("Antenas disponibles: [{Antennas}]", string.Join(", ", availableAntennas));
        _logger.LogInformation("Modelo: {Model}", caps.ModelName ?? "N/A");
        _logger.LogInformation("Firmware: {Fw}", caps.FirwareVersion ?? "N/A");

        var txValues = caps.TransmitPowerLevelValues;
        if (txValues != null && txValues.Length > 0)
        {
            _logger.LogInformation("Tabla potencias TX ({Count} valores):", txValues.Length);
            for (int i = 0; i < txValues.Length; i++)
                _logger.LogInformation("  Indice {Idx} = {Val} (0.1 dBm)", i, txValues[i]);
        }

        var rxValues = caps.ReceiveSensitivityValues;
        if (rxValues != null && rxValues.Length > 0)
        {
            _logger.LogInformation("Tabla sensibilidad RX ({Count} valores): [{Vals}]",
                rxValues.Length, string.Join(", ", rxValues));
        }

        try
        {
            var rfModes = caps.RFModes[0];
            _logger.LogInformation("RF Modes disponibles ({Count}):", rfModes.Length);
            for (int i = 0; i < rfModes.Length; i++)
            {
                var m = rfModes[i];
                _logger.LogInformation(
                    "  [{Idx}] ModeID={ModeId} Modulation={Mod} FLM={Flm} DivideRatio={Dr} BDR={Bdr} SpectralMask={Sm}",
                    i, m.ModeIdentifier, m.Modulation, m.ForwardLinkModulationType,
                    m.DivideRatio, m.BdrValue, m.SpectralMaskIndicator);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("No se pudieron leer RF Modes: {Msg}", ex.Message);
        }

        _logger.LogInformation("=== FIN CAPACIDADES ===");
    }

    private void ConfigureAntenna(int antennaId)
    {
        if (_reader == null) return;

        _logger.LogInformation("--- Configurando antena {Id} ---", antennaId);

        var caps = _reader.ReaderCapabilities;
        var txValues = caps.TransmitPowerLevelValues;

        int powerIndex = txValues != null && txValues.Length > 0
            ? Math.Min(30, txValues.Length - 1)
            : 0;
        _logger.LogInformation("Potencia: indice {Idx} (valor tabla: {Val})",
            powerIndex, txValues != null && powerIndex < txValues.Length ? txValues[powerIndex] : "N/A");

        uint rfModeIndex = SelectDenseReaderMode();
        _logger.LogInformation("RF Mode: indice {Idx}", rfModeIndex);

        var rfConfig = _reader.Config.Antennas[antennaId].GetRfConfig();
        rfConfig.TransmitPowerIndex = (ushort)powerIndex;
        rfConfig.RfModeTableIndex = rfModeIndex;
        _reader.Config.Antennas[antennaId].SetRfConfig(rfConfig);
        _logger.LogInformation("SetRfConfig OK: Power={Pwr}, RFMode={Mode}, StopTrigger=NONE (continuous)",
            powerIndex, rfModeIndex);

        var sing = _reader.Config.Antennas[antennaId].GetSingulationControl();
        sing.Session = SESSION.SESSION_S1;
        sing.TagPopulation = 32;
        sing.TagTransitTime = 0;
        sing.Action.PerformStateAwareSingulationAction = false;
        _reader.Config.Antennas[antennaId].SetSingulationControl(sing);
        _logger.LogInformation("Singulation: Session=S1, TagPopulation=32, TransitTime=0");

        var cfg = _reader.Config.Antennas[antennaId].GetConfig();
        _logger.LogInformation("Config leida: RxSens={Rx}, TxPower={Tx}, FreqIdx={Freq}",
            cfg.ReceiveSensitivityIndex, cfg.TransmitPowerIndex, cfg.TransmitFrequencyIndex);

        _logger.LogInformation("--- Antena {Id} configurada ---", antennaId);
    }

    private uint SelectDenseReaderMode()
    {
        if (_reader == null) return 0;

        try
        {
            var rfModes = _reader.ReaderCapabilities.RFModes[0];
            if (rfModes == null || rfModes.Length == 0) return 0;

            int bestIndex = rfModes.Length - 1;
            for (int i = rfModes.Length - 1; i >= 0; i--)
            {
                var modStr = rfModes[i].Modulation.ToString();
                if (modStr == "MV_8")
                {
                    bestIndex = i;
                    break;
                }
            }
            if (bestIndex == rfModes.Length - 1 && rfModes[bestIndex].Modulation.ToString() != "MV_8")
            {
                for (int i = rfModes.Length - 1; i >= 0; i--)
                {
                    if (rfModes[i].Modulation.ToString() == "MV_4")
                    {
                        bestIndex = i;
                        break;
                    }
                }
            }

            _logger.LogInformation("RF Mode seleccionado: [{Idx}] {Desc}",
                bestIndex, rfModes[bestIndex].Modulation);
            return (uint)bestIndex;
        }
        catch
        {
            return 0;
        }
    }

    private void OnReadNotify(object? sender, Events.ReadEventArgs e)
    {
        _logger.LogInformation("OnReadNotify invoked. reader={ReaderNull}, stopPolling={Stop}",
            _reader == null, _stopPolling);

        if (_reader == null || _stopPolling) return;

        try
        {
            var rawTags = _reader.Actions.GetReadTags(100);
            if (rawTags == null || rawTags.Length == 0)
            {
                _logger.LogInformation("ReadNotify disparado: 0 tags");
                return;
            }

            _logger.LogInformation("ReadNotify: {Count} tags raw recibidos", rawTags.Length);

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

                _logger.LogInformation(
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

    private void OnStatusNotify(object? sender, Events.StatusEventArgs e)
    {
        if (_reader == null) return;

        try
        {
            var eventData = e.StatusEventData.StatusEventType;
            _logger.LogInformation("StatusNotify: {EventType}", eventData);

            switch (eventData)
            {
                case Events.STATUS_EVENT_TYPE.INVENTORY_START_EVENT:
                    _logger.LogInformation("SDK: Inventario INICIADO por reader");
                    break;
                case Events.STATUS_EVENT_TYPE.INVENTORY_STOP_EVENT:
                    _logger.LogWarning("SDK: Inventario DETENIDO por reader");
                    break;
                case Events.STATUS_EVENT_TYPE.DISCONNECTION_EVENT:
                    _logger.LogError("SDK: Reader DESCONECTADO");
                    break;
                case Events.STATUS_EVENT_TYPE.READER_EXCEPTION_EVENT:
                    _logger.LogError("SDK: Excepción del reader");
                    break;
                case Events.STATUS_EVENT_TYPE.BUFFER_FULL_EVENT:
                    _logger.LogWarning("SDK: Buffer LLENO - tags perdidos");
                    break;
                case Events.STATUS_EVENT_TYPE.BUFFER_FULL_WARNING_EVENT:
                    _logger.LogWarning("SDK: Buffer casi lleno");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error procesando StatusNotify");
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

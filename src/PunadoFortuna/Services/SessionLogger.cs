using PunadoFortuna.Models;

namespace PunadoFortuna.Services;

public class SessionLogger
{
    private readonly string _logDir;
    private StreamWriter? _writer;
    private readonly object _lock = new();

    public SessionLogger(string logDir = "logs")
    {
        _logDir = logDir;
    }

    public void StartSession()
    {
        Directory.CreateDirectory(_logDir);
        var filename = Path.Combine(_logDir, $"session_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.log");
        _writer = new StreamWriter(filename, append: false);
        _writer.AutoFlush = true;
        LogRaw("SESSION_START", $"Log started at {DateTimeOffset.UtcNow:O}");
    }

    public void StopSession()
    {
        lock (_lock)
        {
            LogRaw("SESSION_END", $"Log ended at {DateTimeOffset.UtcNow:O}");
            _writer?.Dispose();
            _writer = null;
        }
    }

    public void LogRaw(string eventType, string data)
    {
        lock (_lock)
        {
            _writer?.WriteLine($"[{DateTimeOffset.UtcNow:O}] {eventType} | {data}");
        }
    }

    public void LogTagReads(List<TagRead> tags)
    {
        lock (_lock)
        {
            if (_writer == null) return;
            foreach (var tag in tags)
            {
                _writer.WriteLine(
                    $"[{tag.Timestamp:O}] TAG | EPC:{tag.Epc} ANT:{tag.AntennaId} RSSI:{tag.PeakRssi} COUNT:{tag.SeenCount} PHASE:{tag.Phase} CH:{tag.ChannelIndex}");
            }
        }
    }

    public void LogGameState(GameState state)
    {
        LogRaw("GAME_STATE",
            $"Phase:{state.Phase} Count:{state.TagCount}/{state.TotalChips} " +
            $"Colors:{string.Join(",", state.ColorBreakdown.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }
}

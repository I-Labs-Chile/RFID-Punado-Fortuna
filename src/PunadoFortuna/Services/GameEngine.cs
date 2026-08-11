using PunadoFortuna.Models;

namespace PunadoFortuna.Services;

public class GameEngine
{
    private readonly ILogger<GameEngine> _logger;
    private readonly SessionLogger _sessionLogger;
    private readonly Dictionary<string, string> _epcColorMap;

    private GamePhase _phase = GamePhase.WAITING;
    private readonly HashSet<string> _presentEpcs = new();
    private int _totalKnownChips;
    private DateTimeOffset? _stableAt;
    private readonly TimeSpan _stabilityWindow = TimeSpan.FromSeconds(2);
    private bool _wasStable;

    public event EventHandler<GameState>? StateChanged;

    public GameEngine(
        ILogger<GameEngine> logger,
        SessionLogger sessionLogger,
        List<ChipMapping> chipMappings,
        Dictionary<string, string>? colorMap = null)
    {
        _logger = logger;
        _sessionLogger = sessionLogger;
        _totalKnownChips = chipMappings.Count;
        _epcColorMap = colorMap ?? new Dictionary<string, string>();
    }

    public void ProcessTags(List<TagRead> tags)
    {
        if (_phase != GamePhase.WAITING) return;

        var seen = tags.Select(t => t.Epc).Distinct().ToHashSet();
        bool changed = false;

        foreach (var epc in seen)
        {
            if (_presentEpcs.Add(epc))
                changed = true;
        }

        if (changed)
        {
            // El set cambió → reiniciar ventana de estabilidad
            _stableAt = null;
            _wasStable = false;
            _logger.LogDebug("Tags presentes: {Count} (inestable)", _presentEpcs.Count);
        }

        // Si hay tags y no estamos marcando estabilidad todavía, iniciar timer
        if (_presentEpcs.Count > 0 && _stableAt == null)
        {
            _stableAt = DateTimeOffset.UtcNow;
        }

        // Verificar si alcanzamos estabilidad
        bool nowStable = _presentEpcs.Count > 0
            && _stableAt.HasValue
            && (DateTimeOffset.UtcNow - _stableAt.Value) >= _stabilityWindow;

        if (nowStable != _wasStable || changed && _presentEpcs.Count == 0)
        {
            _wasStable = nowStable;
            EmitState();
        }
    }

    public void AdvancePhase()
    {
        switch (_phase)
        {
            case GamePhase.WAITING:
                _phase = GamePhase.REVEAL_COUNT;
                _sessionLogger.LogRaw("GAME", $"REVEAL_COUNT: {_presentEpcs.Count} tags");
                break;

            case GamePhase.REVEAL_COUNT:
                _phase = GamePhase.GUESS_COLORS;
                _sessionLogger.LogRaw("GAME", "GUESS_COLORS phase");
                break;

            case GamePhase.GUESS_COLORS:
                _phase = GamePhase.REVEAL_COLORS;
                _sessionLogger.LogRaw("GAME", $"REVEAL_COLORS: {string.Join(", ", GetColorBreakdown().Select(kv => $"{kv.Key}={kv.Value}"))}");
                break;

            case GamePhase.REVEAL_COLORS:
                Reset();
                return;
        }

        EmitState();
    }

    public void Reset()
    {
        _phase = GamePhase.WAITING;
        _presentEpcs.Clear();
        _stableAt = null;
        _wasStable = false;
        _sessionLogger.LogRaw("GAME", "RESET");
        EmitState();
    }

    public GameState GetState()
    {
        var breakdown = GetColorBreakdown();
        bool isStable = _presentEpcs.Count > 0
            && _stableAt.HasValue
            && (DateTimeOffset.UtcNow - _stableAt.Value) >= _stabilityWindow;

        return new GameState
        {
            Phase = _phase.ToString(),
            TagCount = _presentEpcs.Count,
            ColorBreakdown = breakdown,
            PresentChips = _presentEpcs.Count,
            TotalChips = _totalKnownChips,
            PresentEpcs = _presentEpcs.ToList(),
            IsStable = isStable,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private Dictionary<string, int> GetColorBreakdown()
    {
        var breakdown = new Dictionary<string, int>();
        foreach (var epc in _presentEpcs)
        {
            var color = _epcColorMap.TryGetValue(epc, out var c) ? c : "desconocido";
            if (!breakdown.ContainsKey(color))
                breakdown[color] = 0;
            breakdown[color]++;
        }
        return breakdown;
    }

    private void EmitState()
    {
        _sessionLogger.LogGameState(GetState());
        StateChanged?.Invoke(this, GetState());
    }
}

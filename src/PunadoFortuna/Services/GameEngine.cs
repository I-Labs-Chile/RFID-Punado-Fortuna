using PunadoFortuna.Models;

namespace PunadoFortuna.Services;

public class GameEngine
{
    private readonly ILogger<GameEngine> _logger;
    private readonly SessionLogger _sessionLogger;
    private readonly Dictionary<string, string> _epcColorMap;

    private GamePhase _phase = GamePhase.WAITING;
    private readonly Dictionary<string, int> _epcSeen = new();
    private readonly HashSet<string> _presentEpcs = new();
    private int _totalKnownChips;
    private DateTimeOffset? _stableAt;
    private readonly TimeSpan _stabilityWindow = TimeSpan.FromSeconds(3);
    private bool _wasStable;
    private const int MinObservations = 2;
    private const int MaxObservations = 5;

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
            if (!_epcSeen.ContainsKey(epc))
                _epcSeen[epc] = 0;
            _epcSeen[epc]++;

            if (_epcSeen[epc] >= MinObservations)
            {
                if (_presentEpcs.Add(epc))
                    changed = true;
            }
        }

        if (changed)
        {
            _stableAt = null;
            _wasStable = false;
        }

        if (_presentEpcs.Count > 0 && _stableAt == null)
            _stableAt = DateTimeOffset.UtcNow;

        bool nowStable = _presentEpcs.Count > 0
            && _stableAt.HasValue
            && (DateTimeOffset.UtcNow - _stableAt.Value) >= _stabilityWindow;

        if (nowStable != _wasStable || (changed && _presentEpcs.Count == 0))
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
                break;
            case GamePhase.REVEAL_COUNT:
                _phase = GamePhase.GUESS_COLORS;
                break;
            case GamePhase.GUESS_COLORS:
                _phase = GamePhase.REVEAL_COLORS;
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
        _epcSeen.Clear();
        _stableAt = null;
        _wasStable = false;
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
        var state = GetState();
        _sessionLogger.LogGameState(state);
        StateChanged?.Invoke(this, state);
    }
}

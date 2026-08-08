using PunadoFortuna.Models;

namespace PunadoFortuna.Services;

public class GameEngine
{
    private readonly ILogger<GameEngine> _logger;
    private readonly SessionLogger _sessionLogger;
    private readonly List<ChipMapping> _chipMappings;

    private readonly Dictionary<int, ZoneState> _zones = new();

    private readonly int _graceWindowCycles = 3;
    private readonly TimeSpan _quietTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _resultDisplayDuration = TimeSpan.FromSeconds(10);

    public class ZoneState
    {
        public int ZonaId { get; set; }
        public MatchState State { get; set; } = MatchState.STANDBY;
        public int Score { get; set; }
        public string? Winner { get; set; }

        public Dictionary<string, int> MissedCycles { get; set; } = new();
        public HashSet<string> AbsentChips { get; set; } = new();
        public HashSet<string> PresentChips { get; set; } = new();

        public DateTimeOffset LastChangeAt { get; set; }
        public DateTimeOffset? ResultStartedAt { get; set; }

        public bool WasDirty { get; set; }
    }

    public event EventHandler<GameState>? StateChanged;

    public GameEngine(
        ILogger<GameEngine> logger,
        SessionLogger sessionLogger,
        List<ChipMapping> chipMappings)
    {
        _logger = logger;
        _sessionLogger = sessionLogger;
        _chipMappings = chipMappings;

        foreach (var zonaId in chipMappings.Select(c => c.ZonaId).Distinct())
        {
            var chips = chipMappings.Where(c => c.ZonaId == zonaId).ToList();
            _zones[zonaId] = new ZoneState
            {
                ZonaId = zonaId,
                LastChangeAt = DateTimeOffset.UtcNow
            };
        }
    }

    public void ProcessTagCycle(List<TagRead> tags)
    {
        var tagsByZone = tags.GroupBy(t => (int)t.AntennaId);

        foreach (var zona in _zones.Values)
        {
            if (zona.State == MatchState.RESULT)
            {
                if (zona.ResultStartedAt.HasValue &&
                    DateTimeOffset.UtcNow - zona.ResultStartedAt.Value > _resultDisplayDuration)
                {
                    TransitionTo(zona, MatchState.STANDBY);
                }
                continue;
            }

            var zonaChips = _chipMappings.Where(c => c.ZonaId == zona.ZonaId).ToList();
            var seenEpcs = tagsByZone
                .FirstOrDefault(g => g.Key == zona.ZonaId)?
                .Select(t => t.Epc)
                .ToHashSet() ?? new HashSet<string>();

            bool changed = false;

            foreach (var chip in zonaChips)
            {
                if (seenEpcs.Contains(chip.Epc))
                {
                    zona.MissedCycles[chip.Epc] = 0;
                    if (zona.AbsentChips.Contains(chip.Epc))
                    {
                        zona.AbsentChips.Remove(chip.Epc);
                        zona.PresentChips.Add(chip.Epc);
                        changed = true;
                    }
                    else
                    {
                        zona.PresentChips.Add(chip.Epc);
                    }
                }
                else
                {
                    zona.PresentChips.Remove(chip.Epc);

                    if (!zona.MissedCycles.ContainsKey(chip.Epc))
                        zona.MissedCycles[chip.Epc] = 0;
                    zona.MissedCycles[chip.Epc]++;

                    if (zona.MissedCycles[chip.Epc] > _graceWindowCycles)
                    {
                        if (!zona.AbsentChips.Contains(chip.Epc))
                        {
                            zona.AbsentChips.Add(chip.Epc);
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                zona.LastChangeAt = DateTimeOffset.UtcNow;
                zona.WasDirty = true;

                if (zona.State == MatchState.STANDBY)
                {
                    TransitionTo(zona, MatchState.ACTIVE);
                    _sessionLogger.LogRaw("STATE_CHANGE", $"Zone {zona.ZonaId}: STANDBY -> ACTIVE");
                }
            }

            int newScore = zonaChips
                .Where(c => zona.AbsentChips.Contains(c.Epc))
                .Sum(c => c.Valor);

            if (newScore != zona.Score)
            {
                zona.Score = newScore;
                EmitState(zona);
            }

            if (zona.State == MatchState.ACTIVE && zona.WasDirty)
            {
                var timeSinceChange = DateTimeOffset.UtcNow - zona.LastChangeAt;

                if (timeSinceChange >= _quietTimeout)
                {
                    TransitionTo(zona, MatchState.RESULT);
                    zona.Winner = $"Jugador {zona.ZonaId}";
                    zona.ResultStartedAt = DateTimeOffset.UtcNow;
                    _sessionLogger.LogRaw("STATE_CHANGE",
                        $"Zone {zona.ZonaId}: ACTIVE -> RESULT | Score: {zona.Score} | Chips ausentes: {string.Join(",", zona.AbsentChips)}");
                    _logger.LogInformation(
                        "Zona {ZonaId}: RESULT - Score: {Score}, Ausentes: {Count}",
                        zona.ZonaId, zona.Score, zona.AbsentChips.Count);
                }
            }
        }
    }

    public void ForceReset(int zonaId)
    {
        if (_zones.TryGetValue(zonaId, out var zona))
        {
            _logger.LogWarning("Reset manual de zona {ZonaId}", zonaId);
            _sessionLogger.LogRaw("MANUAL_RESET", $"Zone {zonaId} forced to STANDBY");
            TransitionTo(zona, MatchState.STANDBY);
        }
    }

    public void ForceResetAll()
    {
        foreach (var zona in _zones.Values)
        {
            ForceReset(zona.ZonaId);
        }
    }

    public GameState? GetZoneState(int zonaId)
    {
        if (!_zones.TryGetValue(zonaId, out var zona)) return null;

        var zonaChips = _chipMappings.Where(c => c.ZonaId == zonaId).ToList();

        return new GameState
        {
            ZonaId = zona.ZonaId,
            Score = zona.Score,
            MatchState = zona.State,
            Winner = zona.Winner,
            TotalChips = zonaChips.Count,
            PresentChips = zona.PresentChips.Count,
            AbsentChips = zona.AbsentChips.Count,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    public List<GameState> GetAllZoneStates()
    {
        return _zones.Keys.Select(GetZoneState).OfType<GameState>().ToList();
    }

    private void TransitionTo(ZoneState zona, MatchState newState)
    {
        var oldState = zona.State;
        zona.State = newState;

        if (newState == MatchState.STANDBY)
        {
            zona.Score = 0;
            zona.Winner = null;
            zona.AbsentChips.Clear();
            zona.PresentChips.Clear();
            zona.MissedCycles.Clear();
            zona.ResultStartedAt = null;
            zona.WasDirty = false;
            zona.LastChangeAt = DateTimeOffset.UtcNow;
        }

        EmitState(zona);
    }

    private void EmitState(ZoneState zona)
    {
        var state = GetZoneState(zona.ZonaId);
        if (state != null)
        {
            StateChanged?.Invoke(this, state);
        }
    }
}

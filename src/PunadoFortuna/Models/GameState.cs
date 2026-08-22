namespace PunadoFortuna.Models;

public enum GamePhase
{
    WAITING,
    REVEAL_COUNT,
    GUESS_COLORS,
    REVEAL_COLORS
}

public class GameState
{
    public string Phase { get; set; } = "WAITING";
    public int TagCount { get; set; }
    public Dictionary<string, int> ColorBreakdown { get; set; } = new();
    public int PresentChips { get; set; }
    public int TotalChips { get; set; }
    public List<string> PresentEpcs { get; set; } = new();
    public bool IsStable { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

namespace PunadoFortuna.Models;

public enum MatchState
{
    STANDBY,
    ACTIVE,
    RESULT
}

public class GameState
{
    public int ZonaId { get; set; }
    public int Score { get; set; }
    public MatchState MatchState { get; set; } = MatchState.STANDBY;
    public string? Winner { get; set; }
    public int TotalChips { get; set; }
    public int PresentChips { get; set; }
    public int AbsentChips { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

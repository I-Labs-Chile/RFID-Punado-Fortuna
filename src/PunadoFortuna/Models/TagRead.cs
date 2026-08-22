namespace PunadoFortuna.Models;

public class TagRead
{
    public string Epc { get; set; } = string.Empty;
    public short AntennaId { get; set; }
    public short PeakRssi { get; set; }
    public int SeenCount { get; set; }
    public short Phase { get; set; }
    public short ChannelIndex { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

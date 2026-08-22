namespace PunadoFortuna.Models;

public class ChipMapping
{
    public string Epc { get; set; } = string.Empty;
    public int ZonaId { get; set; }
    public int Valor { get; set; }
    public string Descripcion { get; set; } = string.Empty;
}

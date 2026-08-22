using System.Net;

namespace PunadoFortuna.Models;

public class DeviceDiscoveryResult
{
    public string? IpAddress { get; init; }
    public int Port { get; init; } = 5084;
    public string? Hostname { get; init; }
    public string DiscoveryMethod { get; init; } = "unknown";
    public bool LLRPReachable { get; init; }
    public bool HttpReachable { get; init; }
    public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.UtcNow;
    public List<string> Diagnostics { get; init; } = new();

    public static DeviceDiscoveryResult NotFound => new()
    {
        IpAddress = null,
        Port = 5084,
        DiscoveryMethod = "none",
        Diagnostics = { "No se encontró ningún dispositivo FX9600 en la red" }
    };

    public static DeviceDiscoveryResult FromConfig(string ip, int port = 5084) => new()
    {
        IpAddress = ip,
        Port = port,
        DiscoveryMethod = "static_config",
        Diagnostics = { $"Usando IP fija desde configuración: {ip}:{port}" }
    };
}

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PunadoFortuna.Models;
using Zeroconf;

namespace PunadoFortuna.Services;

public class DeviceDiscoveryService
{
    private readonly ILogger<DeviceDiscoveryService> _logger;
    private readonly TimeSpan _pingTimeout;
    private readonly TimeSpan _tcpTimeout;
    private readonly int _maxConcurrency;

    public DeviceDiscoveryService(
        ILogger<DeviceDiscoveryService> logger,
        TimeSpan? pingTimeout = null,
        TimeSpan? tcpTimeout = null,
        int maxConcurrency = 50)
    {
        _logger = logger;
        _pingTimeout = pingTimeout ?? TimeSpan.FromMilliseconds(500);
        _tcpTimeout = tcpTimeout ?? TimeSpan.FromSeconds(2);
        _maxConcurrency = maxConcurrency;
    }

    public async Task<DeviceDiscoveryResult> DiscoverAsync(
        string? staticIp = null,
        int port = 5084,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(staticIp) && !staticIp.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Usando IP fija: {Ip}", staticIp);
            var probe = await TcpProbeAsync(staticIp, port, ct);
            var httpOk = await HttpProbeAsync(staticIp, ct);

            return new DeviceDiscoveryResult
            {
                IpAddress = staticIp,
                Port = port,
                DiscoveryMethod = "static_config",
                LLRPReachable = probe,
                HttpReachable = httpOk,
                Diagnostics =
                {
                    $"IP fija: {staticIp}",
                    $"LLRP (:{port}): {(probe ? "OK" : "FALLÓ")}",
                    $"HTTP (:80): {(httpOk ? "OK" : "FALLÓ")}"
                }
            };
        }

        _logger.LogInformation("Iniciando descubrimiento automático del FX9600...");
        var diagnostics = new List<string>();
        var candidates = new Dictionary<string, string>();
        var scannedSubnets = new HashSet<string>();

        var interfaces = GetActiveNetworkInterfaces();
        diagnostics.Add($"Adaptadores de red activos: {interfaces.Count}");

        // Subnets descubiertas de los adaptadores activos
        foreach (var iface in interfaces)
        {
            if (ct.IsCancellationRequested) break;

            diagnostics.Add($"  - {iface.Name}: {iface.IpAddress}/{iface.PrefixLength}");

            var subnet = GetSubnetBase(iface.IpAddress, iface.PrefixLength);
            var broadcast = GetBroadcast(iface.IpAddress, iface.PrefixLength);
            scannedSubnets.Add(subnet);

            diagnostics.Add($"    Barriendo subnet {subnet}/...");

            var pingResults = await PingSweepAsync(subnet, broadcast, iface.IpAddress, ct);
            foreach (var ip in pingResults)
            {
                if (!candidates.ContainsKey(ip))
                    candidates[ip] = "ping_sweep";
            }
        }

        // Subnets LAN comunes (aunque no haya adaptador activo en esa subnet)
        var commonSubnets = new[] { "192.168.100.0", "192.168.1.0", "192.168.0.0" };
        foreach (var subnet in commonSubnets)
        {
            if (ct.IsCancellationRequested) break;
            if (scannedSubnets.Contains(subnet)) continue;

            diagnostics.Add($"    Barriendo subnet común {subnet}/24...");
            var broadcast = GetBroadcast(subnet, 24);
            var centerIp = subnet.Replace(".0", ".100");

            var pingResults = await PingSweepAsync(subnet, broadcast, centerIp, ct);
            foreach (var ip in pingResults)
            {
                if (!candidates.ContainsKey(ip))
                {
                    candidates[ip] = "common_sweep";
                    diagnostics.Add($"      Ping OK: {ip}");
                }
            }
        }

        diagnostics.Add($"IPs que respondieron ping: {candidates.Count}");

        // mDNS discovery with hard timeout (no bloquear si Bonjour no está)
        if (!ct.IsCancellationRequested)
        {
            try
            {
                var mdnsTask = MdnsDiscoveryAsync(ct);
                var timeoutTask = Task.Delay(6000, ct);
                var completed = await Task.WhenAny(mdnsTask, timeoutTask);

                if (completed == mdnsTask)
                {
                    var mdnsIps = await mdnsTask;
                    foreach (var ip in mdnsIps)
                    {
                        if (!candidates.ContainsKey(ip))
                            candidates[ip] = "mdns";
                    }
                    diagnostics.Add($"IPs descubiertas vía mDNS: {mdnsIps.Count}");
                }
                else
                {
                    diagnostics.Add("mDNS: timeout (Bonjour probablemente no instalado)");
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add($"mDNS error: {ex.Message}");
            }
        }

        diagnostics.Add($"Total candidatos a probar: {candidates.Count}");

        if (candidates.Count == 0)
        {
            diagnostics.Add("No se encontraron candidatos. Verificá que el FX9600 esté encendido y conectado.");
            return new DeviceDiscoveryResult
            {
                IpAddress = null,
                Port = port,
                DiscoveryMethod = "auto",
                Diagnostics = diagnostics
            };
        }

        // Probar todos los candidatos en paralelo
        var probeTasks = candidates.Select(async (kvp) =>
        {
            var (ip, method) = (kvp.Key, kvp.Value);
            diagnostics.Add($"Probando {ip} (descubierto vía {method})...");
            var llrpOk = await TcpProbeAsync(ip, port, ct);
            var httpOk = await HttpProbeAsync(ip, ct);

            diagnostics.Add($"  LLRP :{port}: {(llrpOk ? "OK" : "FALLÓ")}");
            diagnostics.Add($"  HTTP :80: {(httpOk ? "OK" : "FALLÓ")}");

            return new { ip, method, llrpOk, httpOk };
        }).ToList();

        var results = await Task.WhenAll(probeTasks);

        var found = results.FirstOrDefault(r => r.llrpOk);
        if (found != null)
        {
            diagnostics.Add($">>>>> FX9600 ENCONTRADO en {found.ip}:{port}");
            return new DeviceDiscoveryResult
            {
                IpAddress = found.ip,
                Port = port,
                DiscoveryMethod = $"auto_{found.method}",
                LLRPReachable = true,
                HttpReachable = found.httpOk,
                Diagnostics = diagnostics
            };
        }

        diagnostics.Add("Ningún candidato aceptó conexión LLRP. Verificá que el FX9600 esté encendido.");

        return new DeviceDiscoveryResult
        {
            IpAddress = null,
            Port = port,
            DiscoveryMethod = "auto",
            LLRPReachable = false,
            Diagnostics = diagnostics
        };
    }

    public List<NetworkInterfaceInfo> GetActiveNetworkInterfaces()
    {
        var result = new List<NetworkInterfaceInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (nic.Description?.Contains("Virtual", StringComparison.OrdinalIgnoreCase) == true) continue;
            if (nic.Description?.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) == true) continue;

            try
            {
                var ipProps = nic.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                    result.Add(new NetworkInterfaceInfo
                    {
                        Name = nic.Name,
                        Description = nic.Description ?? nic.Name,
                        IpAddress = unicast.Address.ToString(),
                        PrefixLength = GetPrefixLength(unicast.IPv4Mask),
                        Gateway = ipProps.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al leer propiedades de {Name}", nic.Name);
            }
        }

        return result;
    }

    private static IPAddress UintToIp(uint ipNum)
    {
        var b = new byte[4];
        b[0] = (byte)(ipNum >> 24);
        b[1] = (byte)(ipNum >> 16);
        b[2] = (byte)(ipNum >> 8);
        b[3] = (byte)ipNum;
        return new IPAddress(b);
    }

    public async Task<HashSet<string>> PingSweepAsync(
        string subnetBase,
        string broadcast,
        string centerIp,
        CancellationToken ct)
    {
        var results = new HashSet<string>();

        var arpIps = GetArpCacheIPs();
        foreach (var ip in arpIps)
        {
            if (IsInRange(ip, subnetBase, broadcast))
                results.Add(ip);
        }

        var baseIp = IPAddress.Parse(subnetBase);
        var broadcastIp = IPAddress.Parse(broadcast);
        var baseBytes = baseIp.GetAddressBytes();
        var broadcastBytes = broadcastIp.GetAddressBytes();

        var startIp = (uint)(baseBytes[0] << 24 | baseBytes[1] << 16 | baseBytes[2] << 8 | baseBytes[3]);
        var endIp = (uint)(broadcastBytes[0] << 24 | broadcastBytes[1] << 16 | broadcastBytes[2] << 8 | broadcastBytes[3]);
        var totalRange = endIp - startIp;

        const int maxSweep = 512;
        var ipsToScan = new List<uint>();

        if (totalRange <= maxSweep * 2)
        {
            for (uint ipNum = startIp + 1; ipNum < endIp; ipNum++)
                ipsToScan.Add(ipNum);
        }
        else
        {
            var centerBytes = IPAddress.Parse(centerIp).GetAddressBytes();
            var centerUint = (uint)(centerBytes[0] << 24 | centerBytes[1] << 16 | centerBytes[2] << 8 | centerBytes[3]);

            var sweepStart = centerUint > (uint)maxSweep ? centerUint - (uint)maxSweep : startIp + 1;
            var sweepEnd = Math.Min(centerUint + (uint)maxSweep, endIp - 1);

            for (uint ipNum = sweepStart; ipNum <= sweepEnd; ipNum++)
                ipsToScan.Add(ipNum);

            _logger.LogInformation(
                "Subnet grande ({Range} hosts), barriendo {Count} IPs alrededor de {Center} ({Start}..{End})",
                totalRange, ipsToScan.Count, centerIp,
                UintToIp(sweepStart), UintToIp(sweepEnd));
        }

        var semaphore = new SemaphoreSlim(_maxConcurrency);
        var tasks = new List<Task>();

        foreach (var ipNum in ipsToScan)
        {
            if (ct.IsCancellationRequested) break;

            var ip = UintToIp(ipNum);
            var ipStr = ip.ToString();

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await semaphore.WaitAsync(ct);
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(ip, (int)_pingTimeout.TotalMilliseconds);
                    if (reply.Status == IPStatus.Success)
                    {
                        lock (results)
                        {
                            results.Add(ipStr);
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
        return results;
    }

    private static HashSet<string> GetArpCacheIPs()
    {
        var results = new HashSet<string>();
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            var matches = System.Text.RegularExpressions.Regex.Matches(
                output,
                @"(\d+\.\d+\.\d+\.\d+)");

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var ip = match.Groups[1].Value;
                if (!ip.StartsWith("224.") && !ip.StartsWith("239.") && ip != "255.255.255.255")
                {
                    results.Add(ip);
                }
            }
        }
        catch
        {
            // ARP falló silenciosamente, continuamos con ping sweep
        }

        return results;
    }

    private static bool IsInRange(string ip, string subnetBase, string broadcast)
    {
        try
        {
            var ipBytes = IPAddress.Parse(ip).GetAddressBytes();
            var baseBytes = IPAddress.Parse(subnetBase).GetAddressBytes();
            var broadcastBytes = IPAddress.Parse(broadcast).GetAddressBytes();

            var ipUint = (uint)(ipBytes[0] << 24 | ipBytes[1] << 16 | ipBytes[2] << 8 | ipBytes[3]);
            var baseUint = (uint)(baseBytes[0] << 24 | baseBytes[1] << 16 | baseBytes[2] << 8 | baseBytes[3]);
            var broadcastUint = (uint)(broadcastBytes[0] << 24 | broadcastBytes[1] << 16 | broadcastBytes[2] << 8 | broadcastBytes[3]);

            return ipUint >= baseUint && ipUint <= broadcastUint;
        }
        catch
        {
            return false;
        }
    }

    public async Task<HashSet<string>> MdnsDiscoveryAsync(CancellationToken ct)
    {
        var results = new HashSet<string>();

        try
        {
            var domains = await ZeroconfResolver.BrowseDomainsAsync(
                new TimeSpan(0, 0, 0, 3),
                cancellationToken: ct);

            foreach (var domain in domains)
            {
                var responses = await ZeroconfResolver.ResolveAsync(
                    domain.Key,
                    new TimeSpan(0, 0, 0, 2),
                    cancellationToken: ct);

                foreach (var resp in responses)
                {
                    var displayName = resp.DisplayName ?? "";
                    if (displayName.Contains("FX9600", StringComparison.OrdinalIgnoreCase) ||
                        displayName.Contains("zebra", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var ip in resp.IPAddresses)
                        {
                            results.Add(ip);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error en mDNS discovery");
        }

        return results;
    }

    public async Task<bool> TcpProbeAsync(string ip, int port, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_tcpTimeout);

            using var client = new TcpClient();
            await client.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> HttpProbeAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(3);

            var response = await client.GetAsync($"http://{ip}", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(3);
                var response = await client.GetAsync($"https://{ip}", ct);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    private static string GetSubnetBase(string ip, int prefixLength)
    {
        var ipBytes = IPAddress.Parse(ip).GetAddressBytes();
        if (ipBytes.Length != 4) return ip;

        var mask = uint.MaxValue << (32 - prefixLength);
        var ipUint = (uint)(ipBytes[0] << 24 | ipBytes[1] << 16 | ipBytes[2] << 8 | ipBytes[3]);
        var network = ipUint & mask;

        return $"{(network >> 24) & 0xFF}.{(network >> 16) & 0xFF}.{(network >> 8) & 0xFF}.{network & 0xFF}";
    }

    private static string GetBroadcast(string ip, int prefixLength)
    {
        var ipBytes = IPAddress.Parse(ip).GetAddressBytes();
        if (ipBytes.Length != 4) return ip;

        var mask = uint.MaxValue << (32 - prefixLength);
        var ipUint = (uint)(ipBytes[0] << 24 | ipBytes[1] << 16 | ipBytes[2] << 8 | ipBytes[3]);
        var broadcast = ipUint | ~mask;

        return $"{(broadcast >> 24) & 0xFF}.{(broadcast >> 16) & 0xFF}.{(broadcast >> 8) & 0xFF}.{broadcast & 0xFF}";
    }

    private static int GetPrefixLength(IPAddress? mask)
    {
        if (mask == null) return 24;
        var bytes = mask.GetAddressBytes();
        if (bytes.Length != 4) return 24;

        var maskUint = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        var prefix = 0;

        while (maskUint > 0)
        {
            if ((maskUint & 0x80000000) != 0) prefix++;
            maskUint <<= 1;
        }

        return prefix > 0 ? prefix : 24;
    }
}

public class NetworkInterfaceInfo
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string IpAddress { get; init; } = "";
    public int PrefixLength { get; init; }
    public string Gateway { get; init; } = "";
}

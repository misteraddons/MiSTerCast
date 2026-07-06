using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MiSTerCast
{
    class NetworkTargetCandidate
    {
        public string HostName { get; set; }
        public IPAddress Address { get; set; }

        public string DisplayText
        {
            get { return NetworkTargetScanner.FormatDisplayText(HostName, Address); }
        }

        public bool IsLikelyMister
        {
            get { return !String.IsNullOrWhiteSpace(HostName) && HostName.IndexOf("mister", StringComparison.OrdinalIgnoreCase) >= 0; }
        }
    }

    static class NetworkTargetScanner
    {
        private const int DefaultSshPort = 22;
        private const int DefaultConnectTimeoutMilliseconds = 250;
        private const int DefaultMaxConcurrentChecks = 64;
        private const int DefaultMaxHostsPerInterface = 512;

        public static async Task<List<NetworkTargetCandidate>> ScanAsync(Action<string, bool> log = null)
        {
            List<IPAddress> addresses = GetLocalScanAddresses();
            Log(log, "Scanning " + addresses.Count + " local addresses for SSH targets...");

            var semaphore = new SemaphoreSlim(DefaultMaxConcurrentChecks);
            var tasks = addresses.Select(async address =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await ProbeCandidateAsync(address);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            NetworkTargetCandidate[] candidates = await Task.WhenAll(tasks);
            return candidates
                .Where(candidate => candidate != null)
                .OrderByDescending(candidate => candidate.IsLikelyMister)
                .ThenBy(candidate => candidate.DisplayText, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string FormatDisplayText(string hostName, IPAddress address)
        {
            string addressText = address == null ? "" : address.ToString();
            string simpleName = SimplifyHostName(hostName, addressText);
            return String.IsNullOrWhiteSpace(simpleName) ? addressText : simpleName + " (" + addressText + ")";
        }

        public static string ExtractTargetValue(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
                return "";

            string trimmed = text.Trim();
            int open = trimmed.LastIndexOf('(');
            int close = trimmed.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                string inner = trimmed.Substring(open + 1, close - open - 1).Trim();
                IPAddress address;
                if (IPAddress.TryParse(inner, out address))
                    return inner;
            }

            return trimmed;
        }

        public static IEnumerable<IPAddress> BuildScanAddresses(IPAddress localAddress, IPAddress subnetMask, int maxHosts)
        {
            uint local = ToUInt32(localAddress);
            uint mask = ToUInt32(subnetMask);
            uint network = local & mask;
            uint broadcast = network | ~mask;
            ulong usableHosts = broadcast > network ? (ulong)broadcast - network - 1 : 0;

            if (usableHosts == 0)
                yield break;

            if (usableHosts > (ulong)maxHosts)
            {
                network = local & 0xFFFFFF00u;
                broadcast = network | 0x000000FFu;
            }

            for (uint value = network + 1; value < broadcast; value++)
            {
                if (value == local)
                    continue;
                yield return FromUInt32(value);
            }
        }

        private static List<IPAddress> GetLocalScanAddresses()
        {
            var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                IPInterfaceProperties properties;
                try
                {
                    properties = networkInterface.GetIPProperties();
                }
                catch
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address == null ||
                        unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                        unicast.IPv4Mask == null)
                        continue;

                    foreach (IPAddress address in BuildScanAddresses(unicast.Address, unicast.IPv4Mask, DefaultMaxHostsPerInterface))
                        addresses.Add(address.ToString());
                }
            }

            return addresses.Select(IPAddress.Parse).ToList();
        }

        private static async Task<NetworkTargetCandidate> ProbeCandidateAsync(IPAddress address)
        {
            if (!await IsTcpPortOpenAsync(address, DefaultSshPort, DefaultConnectTimeoutMilliseconds))
                return null;

            return new NetworkTargetCandidate
            {
                Address = address,
                HostName = await ResolveHostNameAsync(address)
            };
        }

        private static async Task<bool> IsTcpPortOpenAsync(IPAddress address, int port, int timeoutMilliseconds)
        {
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                Task connectTask = client.ConnectAsync(address, port);
                Task completedTask = await Task.WhenAny(connectTask, Task.Delay(timeoutMilliseconds));
                if (completedTask != connectTask)
                    return false;

                try
                {
                    await connectTask;
                    return client.Connected;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static Task<string> ResolveHostNameAsync(IPAddress address)
        {
            return Task.Run(() =>
            {
                try
                {
                    IPHostEntry entry = Dns.GetHostEntry(address);
                    string dnsName = entry == null ? "" : SimplifyHostName(entry.HostName, address.ToString());
                    if (!String.IsNullOrWhiteSpace(dnsName))
                        return dnsName;
                }
                catch
                {
                }

                return ResolveNetBiosName(address);
            });
        }

        private static string ResolveNetBiosName(IPAddress address)
        {
            string nbtstatPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "nbtstat.exe");
            if (!File.Exists(nbtstatPath))
                nbtstatPath = "nbtstat.exe";

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = nbtstatPath,
                    Arguments = "-A " + address,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (!process.WaitForExit(750))
                    {
                        try { process.Kill(); }
                        catch { }
                        return "";
                    }

                    return ParseNetBiosName(process.StandardOutput.ReadToEnd());
                }
            }
            catch
            {
                return "";
            }
        }

        public static string ParseNetBiosName(string nbtstatOutput)
        {
            if (String.IsNullOrWhiteSpace(nbtstatOutput))
                return "";

            foreach (string rawLine in nbtstatOutput.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 ||
                    line.StartsWith("Name", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("-", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 &&
                    String.Equals(parts[1], "<00>", StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(parts[2], "UNIQUE", StringComparison.OrdinalIgnoreCase))
                    return parts[0];
            }

            return "";
        }

        private static string SimplifyHostName(string hostName, string addressText)
        {
            if (String.IsNullOrWhiteSpace(hostName) ||
                String.Equals(hostName, addressText, StringComparison.OrdinalIgnoreCase))
                return "";

            string trimmed = hostName.Trim().TrimEnd('.');
            if (trimmed.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - ".local".Length);

            int dot = trimmed.IndexOf('.');
            if (dot > 0)
                trimmed = trimmed.Substring(0, dot);

            return trimmed;
        }

        private static void Log(Action<string, bool> log, string message)
        {
            if (log != null)
                log(message, false);
        }

        private static uint ToUInt32(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return ((uint)bytes[0] << 24) |
                ((uint)bytes[1] << 16) |
                ((uint)bytes[2] << 8) |
                bytes[3];
        }

        private static IPAddress FromUInt32(uint value)
        {
            return new IPAddress(new[]
            {
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            });
        }
    }
}

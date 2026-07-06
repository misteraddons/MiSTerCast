using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MiSTerCast
{
    class GroovyMisterProbeResult
    {
        public bool Success { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }
        public string Message { get; set; }
        public UInt32 Frame { get; set; }
        public UInt16 VCount { get; set; }
        public byte StatusBits { get; set; }
    }

    static class GroovyMisterProbe
    {
        public const int DefaultPort = 32100;

        public static async Task<GroovyMisterProbeResult> ProbeAsync(string target, int timeoutMilliseconds = 1000)
        {
            if (String.IsNullOrWhiteSpace(target))
                return Failure(target, DefaultPort, "target is empty");

            IPAddress address;
            try
            {
                address = await Task.Run(() => ResolveIPv4(target.Trim()));
            }
            catch (Exception exception)
            {
                return Failure(target, DefaultPort, "name resolution failed: " + exception.Message);
            }

            using (var udp = new UdpClient())
            {
                try
                {
                    udp.Connect(address, DefaultPort);

                    byte[] initPacket = { 2, 1, 3, 2, 0 };
                    await udp.SendAsync(initPacket, initPacket.Length);

                    var receiveTask = udp.ReceiveAsync();
                    var timeoutTask = Task.Delay(timeoutMilliseconds);
                    var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                    if (completedTask != receiveTask)
                    {
                        return Failure(address.ToString(), DefaultPort, "no UDP ACK from MiSTer");
                    }

                    var response = receiveTask.Result.Buffer;
                    if (response.Length != 13)
                    {
                        return Failure(address.ToString(), DefaultPort, "unexpected UDP response length " + response.Length);
                    }

                    byte[] closePacket = { 1 };
                    await udp.SendAsync(closePacket, closePacket.Length);

                    return new GroovyMisterProbeResult
                    {
                        Success = true,
                        Address = address.ToString(),
                        Port = DefaultPort,
                        Message = "Groovy_MiSTer detected",
                        Frame = BitConverter.ToUInt32(response, 6),
                        VCount = BitConverter.ToUInt16(response, 10),
                        StatusBits = response[12]
                    };
                }
                catch (SocketException exception)
                {
                    return Failure(address.ToString(), DefaultPort, "UDP socket error: " + exception.Message);
                }
            }
        }

        private static GroovyMisterProbeResult Failure(string address, int port, string message)
        {
            return new GroovyMisterProbeResult
            {
                Success = false,
                Address = address,
                Port = port,
                Message = message
            };
        }

        private static IPAddress ResolveIPv4(string target)
        {
            IPAddress address;
            if (IPAddress.TryParse(target, out address))
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    return address;
                throw new InvalidOperationException("target resolved to non-IPv4 address");
            }

            var hostEntry = Dns.GetHostEntry(target);
            var ipv4Address = hostEntry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4Address == null)
                throw new InvalidOperationException("no IPv4 address found");

            return ipv4Address;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Helpers
{
    public static class NetworkPrinterHelper
    {
        /// <summary>
        /// Validates an IPv4 address string.
        /// </summary>
        public static bool IsValidIpAddress(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            return IPAddress.TryParse(ip, out var address) && address.AddressFamily == AddressFamily.InterNetwork;
        }

        /// <summary>
        /// Validates a port number (1-65535).
        /// </summary>
        public static bool IsValidPort(int port) => port > 0 && port <= 65535;

        /// <summary>
        /// Pings the specified IP address to test network connectivity.
        /// </summary>
        public static async Task<(bool Success, string Message)> TestConnectionAsync(string ipAddress, int timeoutMs = 3000)
        {
            try
            {
                if (!IsValidIpAddress(ipAddress))
                    return (false, "Invalid IP address format.");

                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ipAddress, timeoutMs);

                if (reply.Status == IPStatus.Success)
                    return (true, $"Ping successful. Round-trip: {reply.RoundtripTime}ms");

                return (false, $"Ping failed: {reply.Status}");
            }
            catch (Exception ex)
            {
                return (false, $"Ping error: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to connect to a printer's TCP port to verify it is reachable and listening.
        /// </summary>
        public static async Task<(bool Success, string Message)> TestTcpConnectionAsync(string ipAddress, int port, int timeoutMs = 5000)
        {
            try
            {
                if (!IsValidIpAddress(ipAddress))
                    return (false, "Invalid IP address format.");
                if (!IsValidPort(port))
                    return (false, "Invalid port number.");

                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ipAddress, port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));

                if (completed == connectTask && client.Connected)
                    return (true, $"TCP connection to {ipAddress}:{port} successful.");

                return (false, $"Connection to {ipAddress}:{port} timed out.");
            }
            catch (Exception ex)
            {
                return (false, $"TCP connection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends ZPL data to a network printer via TCP.
        /// </summary>
        public static async Task<(bool Success, string Message)> SendZplViaTcpAsync(string ipAddress, int port, string zpl, int timeoutMs = 5000)
        {
            try
            {
                if (!IsValidIpAddress(ipAddress))
                    return (false, "Invalid IP address format.");
                if (!IsValidPort(port))
                    return (false, "Invalid port number.");

                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ipAddress, port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));

                if (completed != connectTask || !client.Connected)
                    return (false, $"Connection to {ipAddress}:{port} timed out.");

                using var stream = client.GetStream();
                var data = Encoding.ASCII.GetBytes(zpl);
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();

                return (true, "ZPL sent successfully via network.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to send ZPL via network: {ex.Message}");
            }
        }

        /// <summary>
        /// Scans a subnet for devices listening on the specified port (typically 9100 for ZPL printers).
        /// Only scans the last octet (x.x.x.1 through x.x.x.254).
        /// </summary>
        public static async Task<List<string>> FindPrintersOnSubnetAsync(string baseIp, int port = 9100, int timeoutMs = 500)
        {
            var found = new List<string>();

            if (string.IsNullOrWhiteSpace(baseIp) || !IsValidIpAddress(baseIp))
                return found;

            // Extract first three octets
            var parts = baseIp.Split('.');
            if (parts.Length != 4) return found;
            var subnet = $"{parts[0]}.{parts[1]}.{parts[2]}";

            var tasks = new List<Task>();
            var lockObj = new object();

            for (int i = 1; i <= 254; i++)
            {
                var ip = $"{subnet}.{i}";
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var client = new TcpClient();
                        var connectTask = client.ConnectAsync(ip, port);
                        var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));

                        if (completed == connectTask && client.Connected)
                        {
                            lock (lockObj) { found.Add(ip); }
                        }
                    }
                    catch
                    {
                        // Ignore unreachable hosts
                    }
                }));
            }

            await Task.WhenAll(tasks);
            found.Sort();
            return found;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Helpers
{
    public static class SerialPortHelper
    {
        public static string[] GetAvailablePorts()
        {
            try
            {
                return SerialPort.GetPortNames();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static List<int> GetBaudRates() => new() { 9600, 19200, 38400, 115200 };
        public static List<int> GetDataBitsOptions() => new() { 7, 8 };
        public static List<string> GetParityOptions() => new() { "None", "Odd", "Even" };
        public static List<string> GetStopBitsOptions() => new() { "1", "2" };
        public static List<string> GetFlowControlOptions() => new() { "None", "XOnXOff", "RTS/CTS" };

        /// <summary>
        /// Attempts to open and close the specified serial port to detect a connected device.
        /// </summary>
        public static async Task<(bool Success, string Message)> DetectPrinterAsync(
            string portName, int baudRate, int dataBits, string parity, string stopBits, string flowControl)
        {
            return await Task.Run(() =>
            {
                SerialPort? port = null;
                try
                {
                    port = new SerialPort(portName)
                    {
                        BaudRate = baudRate,
                        DataBits = dataBits,
                        Parity = ParseParity(parity),
                        StopBits = ParseStopBits(stopBits),
                        Handshake = ParseHandshake(flowControl),
                        ReadTimeout = 3000,
                        WriteTimeout = 3000
                    };

                    port.Open();

                    // Send ZPL host status query
                    port.Write("~HS");

                    // Try reading a response
                    System.Threading.Thread.Sleep(1500);
                    if (port.BytesToRead > 0)
                    {
                        var response = port.ReadExisting();
                        return (true, $"Printer detected on {portName}. Response: {response.Trim()}");
                    }

                    // Port opened successfully but no response - printer may not support status query
                    return (true, $"Port {portName} opened successfully. Printer may be connected (no status response).");
                }
                catch (UnauthorizedAccessException)
                {
                    return (false, $"Port {portName} is in use by another application.");
                }
                catch (Exception ex)
                {
                    return (false, $"Failed to detect printer on {portName}: {ex.Message}");
                }
                finally
                {
                    if (port != null && port.IsOpen)
                    {
                        try { port.Close(); } catch { }
                    }
                    port?.Dispose();
                }
            });
        }

        public static async Task<(bool Success, string Message)> SendZplViaSerialAsync(
            string portName, int baudRate, int dataBits, string parity, string stopBits, string flowControl, string zpl)
        {
            return await Task.Run(() =>
            {
                SerialPort? port = null;
                try
                {
                    port = new SerialPort(portName)
                    {
                        BaudRate = baudRate,
                        DataBits = dataBits,
                        Parity = ParseParity(parity),
                        StopBits = ParseStopBits(stopBits),
                        Handshake = ParseHandshake(flowControl),
                        WriteTimeout = 5000
                    };

                    port.Open();
                    port.Write(zpl);
                    return (true, "ZPL sent successfully via serial port.");
                }
                catch (Exception ex)
                {
                    return (false, $"Failed to send ZPL via {portName}: {ex.Message}");
                }
                finally
                {
                    if (port != null && port.IsOpen)
                    {
                        try { port.Close(); } catch { }
                    }
                    port?.Dispose();
                }
            });
        }

        private static Parity ParseParity(string value) => value switch
        {
            "Odd" => System.IO.Ports.Parity.Odd,
            "Even" => System.IO.Ports.Parity.Even,
            _ => System.IO.Ports.Parity.None
        };

        private static StopBits ParseStopBits(string value) => value switch
        {
            "2" => System.IO.Ports.StopBits.Two,
            _ => System.IO.Ports.StopBits.One
        };

        private static Handshake ParseHandshake(string value) => value switch
        {
            "XOnXOff" => Handshake.XOnXOff,
            "RTS/CTS" => Handshake.RequestToSend,
            _ => Handshake.None
        };
    }
}

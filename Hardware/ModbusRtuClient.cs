using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>Thrown when a Modbus slave answers with an exception function code (0x8x).</summary>
    public class ModbusException : Exception
    {
        public byte Code { get; }

        public ModbusException(byte code)
            : base($"Modbus exception {code}: {Describe(code)}")
        {
            Code = code;
        }

        private static string Describe(byte code) => code switch
        {
            0x01 => "Illegal function",
            0x02 => "Illegal data address",
            0x03 => "Illegal data value",
            0x04 => "Slave device failure",
            0x05 => "Acknowledge",
            0x06 => "Slave device busy",
            0x08 => "Parity error",
            0x0A => "Gateway path unavailable",
            0x0B => "Gateway target failed to respond",
            _ => $"Unknown (0x{code:X2})"
        };
    }

    /// <summary>
    /// Minimal isolated Modbus RTU master over a serial (RS-485) port using only the
    /// standard library. Implements the functions needed by the weighing line today:
    ///   FC02 Read Discrete Inputs (inputs 0-7 listen), FC03 Read Holding Registers, FC06 Write Single Register (outputs 0-5 write).
    /// Legacy: FC01 Read Coils / FC05 Write Single Coil are retained only for diagnostics -
    /// PLC outputs are now holding registers 0-5 (D0-D5) driven exclusively by FC06.
    /// All frames carry the standard CRC-16/MODBUS (poly 0xA001, low byte first).
    /// A semaphore serialises access because an RS-485 bus is strictly half-duplex.
    /// </summary>
    public sealed class ModbusRtuClient : IDisposable
    {
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private SerialPort? _port;
        private PlcConfig _config;

        private const int DefaultTimeoutMs = 1000;

        public ModbusRtuClient(PlcConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public bool IsOpen => _port?.IsOpen == true;

        public PlcConfig Config => _config;

        /// <summary>Replaces the connection parameters. Only allowed while closed.</summary>
        public void Configure(PlcConfig config)
        {
            if (IsOpen)
                throw new InvalidOperationException("Cannot reconfigure an open Modbus connection.");
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        // ---- Connection lifecycle -------------------------------------------------

        public Task OpenAsync()
        {
            return Task.Run(() =>
            {
                if (IsOpen) return;

                var port = new SerialPort(
                    _config.PortName, _config.BaudRate, _config.Parity, _config.DataBits, _config.StopBits)
                {
                    ReadTimeout = DefaultTimeoutMs,
                    WriteTimeout = DefaultTimeoutMs
                };

                try
                {
                    port.Open();
                    _port = port;
                }
                catch
                {
                    port.Dispose();
                    throw;
                }
            });
        }

        public Task CloseAsync()
        {
            return Task.Run(() =>
            {
                try { if (_port != null && _port.IsOpen) _port.Close(); } catch { }
                _port?.Dispose();
                _port = null;
            });
        }

        // ---- Write operations -------------------------------------------------------

        /// <summary>FC05 - write a single coil (digital output). Retained for legacy diagnostics only - outputs now use FC06 registers.</summary>
        [Obsolete("PLC outputs now use FC06 Write Single Register. Use WriteSingleRegisterAsync for all writes.")]
        public async Task WriteSingleCoilAsync(ushort address, bool value, CancellationToken ct = default)
        {
            byte[] request =
            {
                _config.SlaveAddress, 0x05,
                (byte)(address >> 8), (byte)(address & 0xFF),
                value ? (byte)0xFF : (byte)0x00, 0x00
            };

            // Response is an exact echo of the request (8 bytes incl. CRC).
            await TransactAsync(request, expectedTotalLength: 8, ct);
        }

        /// <summary>FC06 - write a single holding register.</summary>
        public async Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken ct = default)
        {
            byte[] request =
            {
                _config.SlaveAddress, 0x06,
                (byte)(address >> 8), (byte)(address & 0xFF),
                (byte)(value >> 8), (byte)(value & 0xFF)
            };

            await TransactAsync(request, expectedTotalLength: 8, ct);
        }

        // ---- Read operations ----------------------------------------------------------

        /// <summary>FC01 - read one coil. Retained for legacy diagnostics - outputs now use FC03 holding registers.</summary>
        [Obsolete("PLC outputs now use FC03 holding registers. Prefer ReadHoldingRegisterAsync / ReadDiscreteInputAsync.")]
        public Task<bool> ReadCoilAsync(ushort address, CancellationToken ct = default)
            => ReadBitAsync(0x01, address, ct);

        /// <summary>FC02 - read one discrete input.</summary>
        public Task<bool> ReadDiscreteInputAsync(ushort address, CancellationToken ct = default)
            => ReadBitAsync(0x02, address, ct);

        /// <summary>FC02 - bulk read discrete inputs (used for auto-listen 0-5). Returns <paramref name="count"/> bits starting at <paramref name="startAddress"/>.</summary>
        public async Task<bool[]> ReadDiscreteInputsAsync(ushort startAddress, ushort count, CancellationToken ct = default)
        {
            if (count == 0 || count > 2000)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be 1..2000 for Modbus FC02.");

            byte[] request =
            {
                _config.SlaveAddress, 0x02,
                (byte)(startAddress >> 8), (byte)(startAddress & 0xFF),
                (byte)(count >> 8), (byte)(count & 0xFF)
            };

            int byteCount = (count + 7) / 8;
            int expectedTotalLength = 3 + byteCount + 2; // header (slave+func+byteCount) + data + CRC
            byte[] response = await TransactAsync(request, expectedTotalLength, ct);

            // response[2] is byteCount; data starts at [3]
            var result = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int byteIndex = i / 8;
                int bitIndex = i % 8;
                result[i] = ((response[3 + byteIndex] >> bitIndex) & 0x01) != 0;
            }
            return result;
        }

        /// <summary>FC03 - read one holding register.</summary>
        public async Task<ushort> ReadHoldingRegisterAsync(ushort address, CancellationToken ct = default)
        {
            byte[] request =
            {
                _config.SlaveAddress, 0x03,
                (byte)(address >> 8), (byte)(address & 0xFF),
                0x00, 0x01 // quantity = 1 register
            };

            // Response: slave, func, byte count (2), value hi, value lo, CRC lo, CRC hi.
            byte[] response = await TransactAsync(request, expectedTotalLength: 7, ct);
            return (ushort)((response[3] << 8) | response[4]);
        }

        private async Task<bool> ReadBitAsync(byte functionCode, ushort address, CancellationToken ct)
        {
            byte[] request =
            {
                _config.SlaveAddress, functionCode,
                (byte)(address >> 8), (byte)(address & 0xFF),
                0x00, 0x01 // quantity = 1 bit
            };

            // Response: slave, func, byte count (1), data byte, CRC lo, CRC hi.
            byte[] response = await TransactAsync(request, expectedTotalLength: 6, ct);
            return (response[3] & 0x01) != 0;
        }

        // ---- Transport core --------------------------------------------------------------

        /// <summary>
        /// Sends a request frame and waits for the expected response, verifying slave id,
        /// function code and CRC. Throws <see cref="ModbusException"/> on exception frames.
        /// Returns the response frame without the trailing CRC.
        /// </summary>
        private async Task<byte[]> TransactAsync(byte[] request, int expectedTotalLength, CancellationToken ct)
        {
            await _ioLock.WaitAsync(ct);
            try
            {
                return await Task.Run(() =>
                {
                    var port = _port ?? throw new InvalidOperationException("Serial port is not open.");
                    if (!port.IsOpen)
                        throw new InvalidOperationException("Serial port is not open.");

                    byte[] frame = BuildFrame(request);

                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();
                    port.Write(frame, 0, frame.Length);

                    // Header: slave id, function code, byte-count/value-dependent third byte.
                    byte[] head = ReadExact(port, 3, DefaultTimeoutMs);

                    if (head[0] != _config.SlaveAddress)
                        throw new IOException($"Response from unexpected slave id {head[0]} (expected {_config.SlaveAddress}).");

                    bool isException = (head[1] & 0x80) != 0;
                    int remaining = isException ? 2 : expectedTotalLength - 3;

                    byte[] rest = ReadExact(port, remaining, DefaultTimeoutMs);
                    byte[] response = head.Concat(rest).ToArray();

                    VerifyCrc(response);

                    if (isException)
                        throw new ModbusException(response[2]);

                    return response;
                }, ct);
            }
            finally
            {
                try { _ioLock.Release(); } catch (ObjectDisposedException) { } catch (SemaphoreFullException) { }
            }
        }

        private static byte[] BuildFrame(byte[] requestBody)
        {
            byte[] crc = ComputeCrc(requestBody, requestBody.Length);
            byte[] frame = new byte[requestBody.Length + 2];
            Buffer.BlockCopy(requestBody, 0, frame, 0, requestBody.Length);
            frame[^2] = crc[0]; // CRC low byte first (Modbus RTU order)
            frame[^1] = crc[1];
            return frame;
        }

        private static void VerifyCrc(byte[] frame)
        {
            if (frame.Length < 4)
                throw new IOException($"Malformed response ({frame.Length} bytes).");

            byte[] crc = ComputeCrc(frame, frame.Length - 2);
            if (frame[^2] != crc[0] || frame[^1] != crc[1])
                throw new IOException("CRC check failed on response frame.");
        }

        /// <summary>CRC-16/MODBUS (reflected polynomial 0xA001, init 0xFFFF).</summary>
        private static byte[] ComputeCrc(IReadOnlyList<byte> data, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }

            return new[] { (byte)(crc & 0xFF), (byte)((crc >> 8) & 0xFF) };
        }

        /// <summary>Blocking exact-length read with an overall deadline.</summary>
        private static byte[] ReadExact(SerialPort port, int count, int timeoutMs)
        {
            var buffer = new byte[count];
            int offset = 0;
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (offset < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Timed out waiting for {count} bytes from the PLC (received {offset}).");

                try
                {
                    int available = port.BytesToRead;
                    if (available <= 0)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    int toRead = Math.Min(count - offset, available);
                    offset += port.Read(buffer, offset, toRead);
                }
                catch (TimeoutException)
                {
                    // Per-read timeouts are expected while polling; loop until deadline.
                }
            }

            return buffer;
        }

        public void Dispose()
        {
            try { if (_port != null && _port.IsOpen) _port.Close(); } catch { }
            _port?.Dispose();
            try { _ioLock.Dispose(); } catch { }
        }
    }
}

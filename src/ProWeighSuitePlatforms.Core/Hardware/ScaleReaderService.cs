using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class ScaleReaderService : IDisposable
    {
        private readonly ScaleSettingsRepository _scaleSettingsRepository;

        // Serial port (SerialPort connection type)
        private SerialPort? _serialPort;

        // TCP client (TCP connection type)
        private TcpClient? _tcpClient;
        private NetworkStream? _tcpStream;

        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isReading;
        private ScaleSettings? _currentSettings;

        // Singleton instance to persist connection across navigations
        private static ScaleReaderService? _sharedInstance;
        private static readonly object _sharedLock = new object();

        // Semaphore to serialise all SIR/SI commands so polling loop and on-demand requests never clash
        private readonly SemaphoreSlim _sicsRequestLock = new SemaphoreSlim(1, 1);

        public event EventHandler<double>? WeightChanged;
        public event EventHandler<string>? ConnectionError;
        /// <summary>Raised for every complete, non-empty line the scale sends (raw, trimmed).</summary>
        public event EventHandler<string>? RawDataReceived;

        public ScaleReaderService(ScaleSettingsRepository scaleSettingsRepository)
        {
            _scaleSettingsRepository = scaleSettingsRepository;
        }

        /// <summary>Returns a shared ScaleReaderService instance.</summary>
        public static ScaleReaderService GetShared(ScaleSettingsRepository scaleSettingsRepository)
        {
            lock (_sharedLock)
            {
                if (_sharedInstance == null)
                    _sharedInstance = new ScaleReaderService(scaleSettingsRepository);
                return _sharedInstance;
            }
        }

        public bool IsReading => _isReading;
        public string? CurrentPortName => _serialPort?.PortName;

        /// <summary>True when the current connection is TCP.</summary>
        public bool IsTcpMode => _currentSettings != null &&
            string.Equals(_currentSettings.ConnectionType, "TCP", StringComparison.OrdinalIgnoreCase);

        public bool IsSicsMode => _currentSettings != null &&
            string.Equals(_currentSettings.Mode, "SICS", StringComparison.OrdinalIgnoreCase);

        public bool IsId510Mode => _currentSettings != null &&
            string.Equals(_currentSettings.Mode, "ID510", StringComparison.OrdinalIgnoreCase);

        public bool IsPolling => _isReading && _cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested;

        /// <summary>
        /// True while the underlying serial/TCP transport is actually open.
        /// Unlike <see cref="IsPolling"/> this turns false when the read loop exits
        /// after a lost connection, so the UI can report the real device state.
        /// </summary>
        public bool IsTransportOpen => IsTcpMode
            ? (_tcpClient?.Connected == true && _tcpStream != null)
            : (_serialPort?.IsOpen == true);

        /// <summary>Settings of the currently open connection (null when never connected).</summary>
        public ScaleSettings? CurrentSettings => _currentSettings;

        /// <summary>
        /// Pauses the SICS/continuous polling loop without closing the transport connection.
        /// Call this when navigating away from the Batch Weighing view.
        /// </summary>
        public void StopPolling()
        {
            if (!_isReading) return;
            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] StopPolling: stopping read loop");
            _isReading = false;
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// For SICS mode: starts a periodic SIR polling loop (every 500 ms).
        /// For continuous modes (Serial/TCP): starts the background read loop.
        /// Call this when the Batch Weighing view becomes active.
        /// </summary>
        public void StartPolling()
        {
            bool transportReady = IsTcpMode
                ? (_tcpClient != null && _tcpClient.Connected && _tcpStream != null)
                : (_serialPort != null && _serialPort.IsOpen);

            if (!transportReady)
            {
                System.Diagnostics.Debug.WriteLine("[ScaleReaderService] StartPolling: transport not ready � skipping");
                return;
            }

            if (_isReading) return;

            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] StartPolling: starting read loop");
            var cts = new CancellationTokenSource();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = cts;
            _isReading = true;

            if (IsSicsMode)
                _ = Task.Run(() => ReadWeightLoopSics(cts.Token));
            else if (IsTcpMode)
                _ = Task.Run(() => ReadWeightLoopContinuousTcp(cts.Token));
            else
                _ = Task.Run(() => ReadWeightLoopContinuous(cts.Token));

            if (IsId510Mode)
                System.Diagnostics.Debug.WriteLine("[ScaleReaderService] ID510 mode: using continuous read loop with ID510 format parsing");
        }

        // ?? Connection ????????????????????????????????????????????????????????

        public async Task<bool> StartReadingAsync(ScaleSettings? settings = null)
        {
            if (settings == null)
            {
                System.Diagnostics.Debug.WriteLine("[ScaleReaderService] No settings provided, loading from repository...");
                settings = await _scaleSettingsRepository.GetAsync();
            }

            if (settings == null)
                throw new InvalidOperationException("No scale settings found");

            bool isTcp = string.Equals(settings.ConnectionType, "TCP", StringComparison.OrdinalIgnoreCase);

            // Validate before attempting to open
            if (isTcp)
            {
                if (string.IsNullOrWhiteSpace(settings.IpAddress))
                    throw new InvalidOperationException("Scale TCP IP address is empty. Please configure the scale IP in Settings.");
                if (settings.Port <= 0)
                    throw new InvalidOperationException("Scale TCP port is invalid. Please configure a valid port number in Settings.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(settings.PortName))
                    throw new InvalidOperationException("Scale COM port name is empty. Please configure the COM port in Settings.");
            }

            // Already connected on the same endpoint � just refresh settings and ensure polling is running
            if (IsConnectedToSameEndpoint(settings))
            {
                System.Diagnostics.Debug.WriteLine("[ScaleReaderService] Already connected on same endpoint � refreshing settings");
                _currentSettings = settings;
                return true;
            }

            // Different endpoint � stop first
            if (_isReading)
            {
                System.Diagnostics.Debug.WriteLine("[ScaleReaderService] Stopping existing connection to open new one...");
                StopReading();
                await Task.Delay(300);
            }

            if (isTcp)
                return await OpenTcpAsync(settings);
            else
                return await OpenSerialPortAsync(settings);
        }

        private bool IsConnectedToSameEndpoint(ScaleSettings settings)
        {
            bool isTcp = string.Equals(settings.ConnectionType, "TCP", StringComparison.OrdinalIgnoreCase);

            if (isTcp)
            {
                return _tcpClient != null && _tcpClient.Connected &&
                       string.Equals(_currentSettings?.IpAddress, settings.IpAddress, StringComparison.OrdinalIgnoreCase) &&
                       _currentSettings?.Port == settings.Port;
            }
            else
            {
                return _serialPort != null && _serialPort.IsOpen &&
                       string.Equals(_serialPort.PortName, settings.PortName, StringComparison.OrdinalIgnoreCase);
            }
        }

        private async Task<bool> OpenTcpAsync(ScaleSettings settings)
        {
            System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] Connecting via TCP to {settings.IpAddress}:{settings.Port} [{settings.Mode} mode]...");
            try
            {
                _tcpClient = new TcpClient();

                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _tcpClient.ConnectAsync(settings.IpAddress!, settings.Port, connectCts.Token);

                _tcpStream = _tcpClient.GetStream();

                _isReading       = false; // polling not started yet � caller must invoke StartPolling()
                _currentSettings = settings;

                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] TCP connected to {settings.IpAddress}:{settings.Port} (polling NOT started yet)");
                return true;
            }
            catch (OperationCanceledException)
            {
                var ex2 = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.TimedOut);
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] TCP connect timed out");
                CloseTcp();
                _isReading       = false;
                _currentSettings = null;
                throw new System.IO.IOException($"TCP connection to {settings.IpAddress}:{settings.Port} timed out after 5 s.", ex2);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] TCP connect failed: {ex.Message}");
                CloseTcp();
                _isReading       = false;
                _currentSettings = null;
                throw;
            }
        }

        private Task<bool> OpenSerialPortAsync(ScaleSettings settings)
        {
            System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] Opening serial port {settings.PortName} [{settings.Mode} mode]...");

            var availablePorts = SerialPort.GetPortNames();
            if (!Array.Exists(availablePorts, p => p.Equals(settings.PortName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new System.IO.IOException(
                    $"Port {settings.PortName} not found. Available ports: {string.Join(", ", availablePorts)}");
            }

            try
            {
                _serialPort = new SerialPort
                {
                    PortName     = settings.PortName,
                    BaudRate     = settings.BaudRate,
                    DataBits     = settings.DataBits,
                    Parity       = (Parity)settings.Parity,
                    StopBits     = (StopBits)settings.StopBits,
                    ReadTimeout  = 2000,
                    WriteTimeout = 1000,
                    NewLine      = "\r\n"
                };

                _serialPort.Open();
                _isReading       = false; // polling not started yet � caller must invoke StartPolling()
                _currentSettings = settings;

                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] Port {settings.PortName} opened (polling NOT started yet)");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] Failed to open port: {ex.Message}");
                _serialPort?.Dispose();
                _serialPort      = null;
                _isReading       = false;
                _currentSettings = null;
                throw;
            }
        }

        public void StopReading()
        {
            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] Stopping reading...");
            _isReading = false;
            _cancellationTokenSource?.Cancel();

            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                    _serialPort.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] Error closing serial port: {ex.Message}");
            }

            _serialPort?.Dispose();
            _serialPort = null;

            CloseTcp();

            // Preserve _currentSettings so IsSicsMode/IsTcpMode stay valid after stop
            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] Connection closed");
        }

        private void CloseTcp()
        {
            try { _tcpStream?.Close(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _tcpStream = null;
            _tcpClient = null;
        }

        // ?? Public commands ???????????????????????????????????????????????????

        public async Task SendImmediateSirAsync()
        {
            if (!IsSicsMode) return;
            bool ready = IsTcpMode
                ? (_tcpClient != null && _tcpClient.Connected)
                : (_serialPort != null && _serialPort.IsOpen);
            if (!ready) return;

            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] Sending immediate SIR...");
            if (await _sicsRequestLock.WaitAsync(500))
            {
                try
                {
                    await SendSicsCommandAndReadAsync("SIR", timeoutMs: 2000);
                }
                finally
                {
                    _sicsRequestLock.Release();
                }
            }
        }

        public async Task<double?> RequestWeightAsync()
        {
            if (_currentSettings == null)
            {
                System.Diagnostics.Debug.WriteLine("[ScaleReaderService] Cannot request weight � not connected");
                return null;
            }

            if (!IsSicsMode)
            {
                System.Diagnostics.Debug.WriteLine("[ScaleReaderService] RequestWeightAsync: mode is not SICS");
                return null;
            }

            await _sicsRequestLock.WaitAsync();
            try
            {
                return await SendSicsCommandAndReadAsync("SI", timeoutMs: 3000);
            }
            finally
            {
                _sicsRequestLock.Release();
            }
        }

        /// <summary>
        /// Sends a raw scale control command (ZI, TI, TAC) over the active transport.
        /// Pauses the SICS polling loop while the command is in flight so they never interleave.
        /// Returns the scale's raw ack line, or null on timeout/error.
        /// </summary>
        public async Task<string?> SendRawCommandAsync(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            bool isTcp = IsTcpMode;

            if (isTcp && (_tcpClient == null || !_tcpClient.Connected || _tcpStream == null))
            {
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] SendRawCommandAsync ({command}): TCP not connected.");
                return null;
            }
            if (!isTcp && (_serialPort == null || !_serialPort.IsOpen))
            {
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] SendRawCommandAsync ({command}): serial port not open.");
                return null;
            }

            // Pause the SICS polling loop for the duration of this command
            bool lockAcquired = await _sicsRequestLock.WaitAsync(3000);
            if (!lockAcquired)
            {
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] SendRawCommandAsync ({command}): could not acquire lock within 3 s.");
                return null;
            }

            try
            {
                if (isTcp)
                {
                    if (_tcpStream!.DataAvailable)
                    {
                        var drain = new byte[512];
                        while (_tcpStream.DataAvailable)
                            await _tcpStream.ReadAsync(drain, 0, drain.Length);
                    }
                    var cmdBytes = Encoding.ASCII.GetBytes(command + "\r\n");
                    await _tcpStream.WriteAsync(cmdBytes, 0, cmdBytes.Length);
                    await _tcpStream.FlushAsync();
                }
                else
                {
                    _serialPort!.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                    var cmdBytes = Encoding.ASCII.GetBytes(command + "\r\n");
                    await Task.Run(() => _serialPort!.Write(cmdBytes, 0, cmdBytes.Length));
                }

                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] Sent command: {command}");

                // Read the ack response (e.g. "Z A", "T A", "TAC A")
                string? response = null;
                try
                {
                    if (isTcp)
                    {
                        var buf = new StringBuilder();
                        response = await ReadTcpLineAsync(buf, DateTime.UtcNow.AddMilliseconds(1500), CancellationToken.None);
                    }
                    else
                    {
                        response = await Task.Run(() =>
                        {
                            _serialPort!.ReadTimeout = 1500;
                            return _serialPort.ReadLine();
                        });
                    }
                }
                catch (TimeoutException)
                {
                    System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] {command}: no ack within 1.5 s.");
                }

                if (response != null)
                    System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] {command} ack: '{response.Trim()}'");

                return response?.Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] SendRawCommandAsync error ({command}): {ex.Message}");
                return null;
            }
            finally
            {
                _sicsRequestLock.Release();
            }
        }

        // ?? Serial read loops ????????????????????????????????????????????????

        /// <summary>
        /// SICS polling loop: sends SIR every 500 ms and fires WeightChanged on each valid response.
        /// Runs while _isReading is true. The semaphore serialises SIR with any on-demand SI requests.
        /// </summary>
        private async Task ReadWeightLoopSics(CancellationToken cancellationToken)
        {
            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] SICS polling loop started");

            while (_isReading && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    bool transportOk = IsTcpMode
                        ? (_tcpClient != null && _tcpClient.Connected && _tcpStream != null)
                        : (_serialPort != null && _serialPort.IsOpen);

                    if (!transportOk)
                    {
                        ConnectionError?.Invoke(this, IsTcpMode
                            ? "TCP connection to scale lost unexpectedly."
                            : "Scale serial port closed unexpectedly.");
                        break;
                    }

                    // Acquire lock within 1 s � if an on-demand SI is running, wait for it
                    if (await _sicsRequestLock.WaitAsync(1000, cancellationToken))
                    {
                        try
                        {
                            await SendSicsCommandAndReadAsync("SIR", timeoutMs: 1500);
                        }
                        finally
                        {
                            _sicsRequestLock.Release();
                        }
                    }

                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] SICS loop error: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
            }

            EndReadLoop(cancellationToken);
            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] SICS polling loop stopped");
        }

        private async Task ReadWeightLoopContinuous(CancellationToken cancellationToken)
        {
            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] Serial continuous loop started");
            int consecutiveErrors = 0;

            while (_isReading && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_serialPort == null || !_serialPort.IsOpen)
                    {
                        ConnectionError?.Invoke(this, "Scale serial port closed unexpectedly.");
                        break;
                    }

                    if (_serialPort.BytesToRead > 0)
                    {
                        var data   = _serialPort.ReadLine();
                        if (!string.IsNullOrWhiteSpace(data))
                            RawDataReceived?.Invoke(this, data.Trim());
                        var weight = ParseWeight(data);
                        if (weight.HasValue) { consecutiveErrors = 0; WeightChanged?.Invoke(this, weight.Value); }
                        else if (++consecutiveErrors >= 10)
                        {
                            ConnectionError?.Invoke(this, $"Cannot parse weight from scale data: '{data?.Trim()}'. Check protocol settings.");
                            consecutiveErrors = 0;
                        }
                    }

                    await Task.Delay(100, cancellationToken);
                }
                catch (TimeoutException) { }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    ConnectionError?.Invoke(this, $"Scale read error: {ex.Message}");
                    await Task.Delay(500, cancellationToken);
                }
            }

            EndReadLoop(cancellationToken);
            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] Serial continuous loop stopped");
        }

        // ?? TCP read loops ????????????????????????????????????????????????????

        private async Task ReadWeightLoopContinuousTcp(CancellationToken cancellationToken)
        {
            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] TCP continuous loop started");
            var lineBuffer = new StringBuilder();
            var readBuf    = new byte[256];
            int consecutiveErrors = 0;

            while (_isReading && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_tcpClient == null || !_tcpClient.Connected || _tcpStream == null)
                    {
                        ConnectionError?.Invoke(this, "TCP connection to scale lost unexpectedly.");
                        break;
                    }

                    if (_tcpStream.DataAvailable)
                    {
                        int bytesRead = await _tcpStream.ReadAsync(readBuf, 0, readBuf.Length, cancellationToken);
                        if (bytesRead > 0)
                        {
                            lineBuffer.Append(Encoding.ASCII.GetString(readBuf, 0, bytesRead));
                            // Process complete lines
                            string raw = lineBuffer.ToString();
                            int newline;
                            while ((newline = IndexOfNewline(raw)) >= 0)
                            {
                                var line = raw[..newline].Trim();
                                raw = raw[(newline + 1)..].TrimStart('\n');
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    RawDataReceived?.Invoke(this, line);
                                    var weight = ParseWeight(line);
                                    if (weight.HasValue) { consecutiveErrors = 0; WeightChanged?.Invoke(this, weight.Value); }
                                    else if (++consecutiveErrors >= 10)
                                    {
                                        ConnectionError?.Invoke(this, $"Cannot parse weight from TCP data: '{line}'. Check protocol settings.");
                                        consecutiveErrors = 0;
                                    }
                                }
                            }
                            lineBuffer.Clear();
                            lineBuffer.Append(raw);
                        }
                    }

                    await Task.Delay(100, cancellationToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    ConnectionError?.Invoke(this, $"TCP read error: {ex.Message}");
                    await Task.Delay(500, cancellationToken);
                }
            }

            EndReadLoop(cancellationToken);
            System.Diagnostics.Debug.WriteLine("[ScaleReaderService] TCP continuous loop stopped");
        }

        /// <summary>
        /// Clears the reading flag when a read loop exits on its own (lost port /
        /// socket). Without this, IsPolling stayed true after a failure and
        /// ScaleConnectionService refused to reconnect.
        /// </summary>
        private void EndReadLoop(CancellationToken cancellationToken)
        {
            try
            {
                if (_cancellationTokenSource != null && _cancellationTokenSource.Token == cancellationToken)
                    _isReading = false;
            }
            catch { }
        }

        private static int IndexOfNewline(string s)
        {
            int r = s.IndexOf('\r');
            int n = s.IndexOf('\n');
            if (r < 0) return n;
            if (n < 0) return r;
            return Math.Min(r, n);
        }

        // ?? SICS command / response ???????????????????????????????????????????

        private async Task<double?> SendSicsCommandAndReadAsync(string command, int timeoutMs = 2000)
        {
            bool isTcp = IsTcpMode;

            if (isTcp && (_tcpClient == null || !_tcpClient.Connected || _tcpStream == null))
            {
                ConnectionError?.Invoke(this, "Cannot send SICS command � TCP connection is not open.");
                return null;
            }
            if (!isTcp && (_serialPort == null || !_serialPort.IsOpen))
            {
                ConnectionError?.Invoke(this, "Cannot send SICS command � serial port is not open.");
                return null;
            }

            try
            {
                var cmdBytes = Encoding.ASCII.GetBytes(command + "\r\n");

                if (isTcp)
                {
                    if (_tcpStream!.DataAvailable)
                    {
                        var drain = new byte[512];
                        while (_tcpStream.DataAvailable)
                            await _tcpStream.ReadAsync(drain, 0, drain.Length);
                    }
                    await _tcpStream.WriteAsync(cmdBytes, 0, cmdBytes.Length);
                    await _tcpStream.FlushAsync();
                }
                else
                {
                    _serialPort!.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                    await Task.Run(() => _serialPort.WriteLine(command));
                }

                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] Sent SICS command: {command}");

                var deadline      = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                var responseLines = new List<string>();
                var lineBuffer    = new StringBuilder();

                while (DateTime.UtcNow < deadline)
                {
                    string? line = null;
                    try
                    {
                        if (isTcp)
                        {
                            line = await ReadTcpLineAsync(lineBuffer, deadline, CancellationToken.None);
                        }
                        else
                        {
                            line = await Task.Run(() =>
                            {
                                _serialPort!.ReadTimeout = timeoutMs;
                                return _serialPort.ReadLine();
                            });
                        }
                    }
                    catch (TimeoutException)
                    {
                        if (DateTime.UtcNow >= deadline) break;
                        await Task.Delay(50);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var trimmedLine = line.Trim();
                        responseLines.Add(trimmedLine);
                        RawDataReceived?.Invoke(this, trimmedLine);
                        System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] SICS line: '{trimmedLine}'");

                        var w = ParseSicsLines(responseLines);
                        if (w.HasValue)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] SICS weight: {w.Value}");
                            WeightChanged?.Invoke(this, w.Value);
                            return w;
                        }
                    }
                }

                var final = ParseSicsLines(responseLines);
                if (final.HasValue)
                {
                    WeightChanged?.Invoke(this, final.Value);
                    System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] SICS final weight: {final.Value}");
                }
                else
                {
                    var rawDump = responseLines.Count > 0
                        ? $"Raw: [{string.Join(" | ", responseLines)}]"
                        : "No response from scale.";
                    System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] No weight parsed. {rawDump}");
                }

                return final;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] SICS command error: {ex.Message}");
                ConnectionError?.Invoke(this, $"SICS command '{command}' failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Reads one complete CR/LF-terminated line from the TCP stream within the deadline.</summary>
        private async Task<string?> ReadTcpLineAsync(StringBuilder buffer, DateTime deadline, CancellationToken ct)
        {
            var readBuf = new byte[256];

            while (DateTime.UtcNow < deadline)
            {
                // Check if we already have a complete line in the buffer
                string current = buffer.ToString();
                int nl = IndexOfNewline(current);
                if (nl >= 0)
                {
                    var line = current[..nl].Trim();
                    buffer.Clear();
                    buffer.Append(current[(nl + 1)..].TrimStart('\n'));
                    return line;
                }

                if (_tcpStream == null || _tcpClient == null || !_tcpClient.Connected)
                    throw new TimeoutException("TCP stream unavailable");

                if (_tcpStream.DataAvailable)
                {
                    int bytesRead = await _tcpStream.ReadAsync(readBuf, 0, readBuf.Length, ct);
                    if (bytesRead > 0)
                        buffer.Append(Encoding.ASCII.GetString(readBuf, 0, bytesRead));
                }
                else
                {
                    await Task.Delay(30, ct);
                }
            }

            throw new TimeoutException("TCP readline timeout");
        }

        // ?? Parsing ???????????????????????????????????????????????????????????

        // Matches date fields ("18-10-2026", "18/Oct/2026", "2026-10-18") and
        // time fields ("23:12:55") so they are never mistaken for a weight.
        private static readonly Regex DateOrTimeTokenRegex = new(
            @"^\d{1,4}[-/.](\d{1,2}|\w{3})[-/.]\d{1,4}$|^\d{1,2}:\d{2}(:\d{2})?$",
            RegexOptions.Compiled);

        private double? ParseWeight(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;

            // Record lines (e.g. IND570 with a configured delimiter) start with date and
            // time fields: "18-10-2026^23:12:55^111^kg^...". Splitting on the delimiter (or
            // whitespace for plain weight streams) and skipping date/time/text tokens makes
            // the first remaining numeric field the weight.
            return ParseWeightFromTokens(TokenizeLine(data, _currentSettings?.Delimiter));
        }

        /// <summary>
        /// Splits a scale line into fields: on the configured delimiter for record lines
        /// (e.g. "18-10-2026^23:12:55^111^kg^..."), otherwise on whitespace.
        /// </summary>
        public static IEnumerable<string> TokenizeLine(string data, string? delimiter)
            => !string.IsNullOrWhiteSpace(delimiter) && data.Contains(delimiter, StringComparison.Ordinal)
                ? data.Split(delimiter)
                : data.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        // Matches the UOM token the scale sends with each record (e.g. "kg", "g", "lb").
        private static readonly Regex UnitTokenRegex = new(
            @"^(kg|g|mg|t|lb|lbs|oz)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Returns the first UOM token ("kg", "g", "lb", ...) in the line, if any.</summary>
        public static string? ParseUnit(string data, string? delimiter)
            => ParseUnitFromTokens(TokenizeLine(data, delimiter));

        /// <summary>Returns the first UOM token ("kg", "g", "lb", ...) among the fields, if any.</summary>
        public static string? ParseUnitFromTokens(IEnumerable<string> tokens)
        {
            foreach (var raw in tokens)
            {
                var token = (raw ?? string.Empty).Trim();
                if (UnitTokenRegex.IsMatch(token)) return token.ToLowerInvariant();
            }
            return null;
        }

        /// <summary>
        /// Returns the first token that is a plain weight number, skipping date tokens
        /// ("18-10-2026"), time tokens ("23:12:55") and text tokens ("kg", "LOT.123").
        /// </summary>
        public static double? ParseWeightFromTokens(IEnumerable<string> tokens)
        {
            foreach (var raw in tokens)
            {
                var token = (raw ?? string.Empty).Trim();
                if (token.Length == 0) continue;
                if (DateOrTimeTokenRegex.IsMatch(token)) continue;
                if (token.Any(char.IsLetter)) continue;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                    return w;
            }
            return null;
        }

        private double? ParseSicsLines(List<string> lines)
        {
            foreach (var line in lines)
            {
                var w = ParseSicsLine(line);
                if (w.HasValue) return w;
            }
            return null;
        }

        private double? ParseSicsLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            var trimmed = line.Trim();
            System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] Parsing SICS line: '{trimmed}'");

            // Try standard SICS patterns (MT SICS):
            //   "S S  +   1.234 kg"  - immediate stable response
            //   "S D  +   1.234 kg"  - dynamic response
            //   "SI S  +   1.234 kg" - immediate inquiry stable
            //   "SI D  +   1.234 kg" - immediate inquiry dynamic
            //   "SIR S  +   1.234 kg"- send weight on request (continuous)
            // Also handle compact/firmware-variant forms with no space between S tokens.
            var m = Regex.Match(trimmed, @"^S(?:IR?|I)?\s+[SD]\s+(.*)", RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(trimmed, @"^S\s*[SD]\s+(.*)", RegexOptions.IgnoreCase);

            if (!m.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] No SICS prefix matched: '{trimmed}'");
                return null;
            }

            var numMatch = Regex.Match(m.Groups[1].Value.Trim(), @"([+-]?\s*\d+[\.,]?\d*)");
            if (!numMatch.Success) return null;

            var numStr = numMatch.Groups[1].Value.Replace(" ", "").Replace(",", ".");
            if (double.TryParse(numStr, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var weight))
            {
                System.Diagnostics.Debug.WriteLine($"[ScaleReaderService] Parsed {weight} from '{trimmed}'");
                return weight;
            }

            return null;
        }

        public void Dispose() => StopReading();
    }
}

using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class SettingsViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _userContext;
        private readonly ScaleConfigService _scaleConfigService;
        private readonly AuditService _auditService;
        public string Title => "Scale Settings";

        // COM port settings
        public ObservableCollection<string> AvailablePorts { get; } = new();
        
        public string ScaleName { get => _scaleName; set => SetProperty(ref _scaleName, value); }
        private string _scaleName = string.Empty;

        public string Location { get => _location; set => SetProperty(ref _location, value); }
        private string _location = string.Empty;

        public string ScaleCode { get => _scaleCode; set => SetProperty(ref _scaleCode, value); }
        private string _scaleCode = string.Empty;

        public string? SelectedPort { get => _selectedPort; set => SetProperty(ref _selectedPort, value); }
        private string? _selectedPort;

        public int BaudRate { get => _baudRate; set => SetProperty(ref _baudRate, value); }
        private int _baudRate = 9600;

        public Parity Parity { get => _parity; set => SetProperty(ref _parity, value); }
        private Parity _parity = Parity.None;

        public int DataBits { get => _dataBits; set => SetProperty(ref _dataBits, value); }
        private int _dataBits = 8;

        public StopBits StopBits { get => _stopBits; set => SetProperty(ref _stopBits, value); }
        private StopBits _stopBits = StopBits.One;

        public string Mode { get => _mode; set => SetProperty(ref _mode, value); }
        private string _mode = "Continuous";

        public bool ZeroReturn { get => _zeroReturn; set => SetProperty(ref _zeroReturn, value); }
        private bool _zeroReturn = false;

        private bool _isAutoCapture = false;
        public bool IsAutoCapture
        {
            get => _isAutoCapture;
            set
            {
                if (SetProperty(ref _isAutoCapture, value))
                {
                    ValidateCaptureInterval();
                }
            }
        }

        private int _captureIntervalMs = 500;
        public int CaptureIntervalMs
        {
            get => _captureIntervalMs;
            set
            {
                if (SetProperty(ref _captureIntervalMs, value))
                {
                    ValidateCaptureInterval();
                }
            }
        }

        private string? _captureIntervalError;
        public string? CaptureIntervalError
        {
            get => _captureIntervalError;
            set => SetProperty(ref _captureIntervalError, value);
        }

        private string _zeroReturnToleranceMinus = string.Empty;
        public string ZeroReturnToleranceMinus
        {
            get => _zeroReturnToleranceMinus;
            set => SetProperty(ref _zeroReturnToleranceMinus, value);
        }

        private string _zeroReturnTolerancePlus = string.Empty;
        public string ZeroReturnTolerancePlus
        {
            get => _zeroReturnTolerancePlus;
            set => SetProperty(ref _zeroReturnTolerancePlus, value);
        }

        // Received data preview
        public string ReceivedData { get => _receivedData; set => SetProperty(ref _receivedData, value); }
        private string _receivedData = string.Empty;

        public ObservableCollection<string> ConnectionTypes { get; } = new() { "SerialPort", "TCP" };

        private string _connectionType = "SerialPort";
        public string ConnectionType
        {
            get => _connectionType;
            set
            {
                if (SetProperty(ref _connectionType, value))
                {
                    RaisePropertyChanged(nameof(IsSerialPort));
                    RaisePropertyChanged(nameof(IsTcp));
                }
            }
        }

        public bool IsSerialPort => ConnectionType == "SerialPort";
        public bool IsTcp => ConnectionType == "TCP";

        private string _ipAddress = string.Empty;
        public string IpAddress { get => _ipAddress; set => SetProperty(ref _ipAddress, value?.Trim() ?? string.Empty); }

        private string _tcpPort = "5000";
        public string TcpPort { get => _tcpPort; set => SetProperty(ref _tcpPort, value?.Trim() ?? string.Empty); }

        public ICommand RefreshPortsCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand SaveScaleSettingsCommand { get; }

        public SettingsViewModel(ICurrentUserContext userContext)
        {
            _userContext = userContext;
            _scaleConfigService = new ScaleConfigService();
            _auditService = new AuditService();

            RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
            TestConnectionCommand = new RelayCommand(async _ => await TestConnectionAsync());
            SaveScaleSettingsCommand = new RelayCommand(async _ => await SaveScaleSettingsAsync());

            RefreshPorts();
            _ = LoadScaleSettingsAsync();
        }

        private async Task LoadScaleSettingsAsync()
        {
            try
            {
                var s = await _scaleConfigService.LoadSettingsAsync();
                if (s != null)
                {
                    ScaleName = s.ScaleName;
                    Location = s.Location;
                    ScaleCode = s.ScaleCode > 0 ? s.ScaleCode.ToString() : string.Empty;
                    ConnectionType = s.ConnectionType ?? "SerialPort";
                    SelectedPort = s.PortName;
                    BaudRate = s.BaudRate;
                    DataBits = s.DataBits;
                    Parity = (Parity)s.Parity;
                    StopBits = (StopBits)s.StopBits;
                    IpAddress = s.IpAddress ?? string.Empty;
                    TcpPort = s.Port > 0 ? s.Port.ToString() : "5000";
                    Mode = s.Mode ?? "Continuous";
                    ZeroReturn = s.ZeroReturn;
                    ZeroReturnToleranceMinus = s.ZeroReturnToleranceMinus > 0 ? s.ZeroReturnToleranceMinus.ToString() : string.Empty;
                    ZeroReturnTolerancePlus = s.ZeroReturnTolerancePlus > 0 ? s.ZeroReturnTolerancePlus.ToString() : string.Empty;
                    IsAutoCapture = s.IsAutoCapture;
                    CaptureIntervalMs = s.CaptureIntervalMs > 0 ? s.CaptureIntervalMs : 500;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to load scale settings: {ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ValidateCaptureInterval()
        {
            if (IsAutoCapture)
            {
                if (CaptureIntervalMs < 1)
                    CaptureIntervalError = "Must be at least 1 ms";
                else if (CaptureIntervalMs > 60000)
                    CaptureIntervalError = "Must be at most 60000 ms";
                else
                    CaptureIntervalError = null;
            }
            else
            {
                CaptureIntervalError = null;
            }
        }

        private void RefreshPorts()
        {
            AvailablePorts.Clear();
            foreach (var p in SerialPort.GetPortNames().OrderBy(n => n))
                AvailablePorts.Add(p);
            if (AvailablePorts.Count > 0 && string.IsNullOrEmpty(SelectedPort))
                SelectedPort = AvailablePorts[0];
        }

        private async Task TestConnectionAsync()
        {
            int.TryParse(TcpPort, out int tcpPortValue);
            ReceivedData = IsSerialPort
                ? $"Connecting to {SelectedPort} at {BaudRate} baud..."
                : $"Connecting to {IpAddress}:{tcpPortValue}...";
            try
            {
                if (IsTcp)
                    await TestTcpConnectionAsync(tcpPortValue);
                else
                    await TestSerialConnectionAsync();
            }
            catch (Exception ex)
            {
                ReceivedData = $"Connection failed: {ex.Message}";
                System.Windows.MessageBox.Show($"Failed to connect: {ex.Message}", "Scale Test",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task TestSerialConnectionAsync()
        {
            if (string.IsNullOrEmpty(SelectedPort)) return;

            const int targetSamples = 10;
            const int testTimeoutSeconds = 10;

            var sb = new StringBuilder();
            int count = 0;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(testTimeoutSeconds));

            await Task.Run(() =>
            {
                using var port = new SerialPort(SelectedPort, BaudRate, Parity, DataBits, StopBits)
                {
                    ReadTimeout  = 2000,
                    WriteTimeout = 1000,
                    NewLine      = "\r\n"
                };

                port.Open();

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    ReceivedData = $"Connected to {SelectedPort}. Reading up to {targetSamples} samples (timeout {testTimeoutSeconds}s)...\n");

                while (count < targetSamples && !cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        string line = port.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            sb.AppendLine(line.Trim());
                            count++;
                            var snapshot = sb.ToString();
                            System.Windows.Application.Current.Dispatcher.Invoke(() => ReceivedData = snapshot);
                        }
                    }
                    catch (TimeoutException) { /* no data yet � keep waiting */ }
                }

                port.Close();
            }, cts.Token).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);

            if (count == 0)
            {
                ReceivedData = $"Connected to {SelectedPort} but no data received within {testTimeoutSeconds}s.\n" +
                               "Check: baud rate, parity, data/stop bits, and that the scale is powered on and transmitting.";
                System.Windows.MessageBox.Show(
                    $"Connected to {SelectedPort} but received no data.\n\nVerify the scale baud rate and protocol settings match.",
                    "No Data Received", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Test successful!\n\n{count} sample(s) received from {SelectedPort}.",
                    "Connection Test", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        private async Task TestTcpConnectionAsync(int tcpPortValue)
        {
            if (string.IsNullOrWhiteSpace(IpAddress)) return;

            const int targetSamples = 10;
            const int testTimeoutSeconds = 10;

            var sb = new StringBuilder();
            int count = 0;
            bool isSicsMode = string.Equals(Mode, "SICS", StringComparison.OrdinalIgnoreCase);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(testTimeoutSeconds));

            try
            {
                await Task.Run(async () =>
                {
                using var client = new TcpClient();
                await client.ConnectAsync(IpAddress, tcpPortValue, cts.Token);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    ReceivedData = isSicsMode
                        ? $"Connected to {IpAddress}:{tcpPortValue} (SICS Mode). Sending SI commands to request weight (timeout {testTimeoutSeconds}s)...\n"
                        : $"Connected to {IpAddress}:{tcpPortValue}. Reading up to {targetSamples} samples (timeout {testTimeoutSeconds}s)...\n");

                using var stream = client.GetStream();
                var lineBuffer = new StringBuilder();
                var readBuf    = new byte[512];

                while (count < targetSamples && !cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (isSicsMode)
                        {
                            // SICS Mode: Send SI command to request weight
                            byte[] command = Encoding.ASCII.GetBytes("SI\r\n");
                            await stream.WriteAsync(command, 0, command.Length, cts.Token);
                            await stream.FlushAsync(cts.Token);

                            // Give scale time to respond
                            await Task.Delay(200, cts.Token);
                        }

                        if (stream.DataAvailable || isSicsMode)
                        {
                            int bytesRead = await stream.ReadAsync(readBuf, 0, readBuf.Length, cts.Token);
                            if (bytesRead > 0)
                            {
                                lineBuffer.Append(Encoding.ASCII.GetString(readBuf, 0, bytesRead));
                                string raw = lineBuffer.ToString();
                                int nl;
                                while ((nl = IndexOfSettingsNewline(raw)) >= 0 && count < targetSamples)
                                {
                                    var line = raw[..nl].Trim();
                                    raw = raw[(nl + 1)..].TrimStart('\n');
                                    if (!string.IsNullOrWhiteSpace(line))
                                    {
                                        sb.AppendLine(line);
                                        count++;
                                        var snapshot = sb.ToString();
                                        System.Windows.Application.Current.Dispatcher.Invoke(() => ReceivedData = snapshot);
                                    }
                                }
                                lineBuffer.Clear();
                                lineBuffer.Append(raw);
                            }
                        }
                        else
                        {
                            await Task.Delay(50, cts.Token);
                        }

                        // For SICS mode, add a delay between commands
                        if (isSicsMode && count < targetSamples)
                        {
                            await Task.Delay(300, cts.Token); // 300ms between requests
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception) { }
                }
            }, cts.Token).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);
            }
            catch (OperationCanceledException)
            {
                // Connect phase hit the 10s timeout (no listener / unreachable host).
                ReceivedData = $"No response from {IpAddress}:{tcpPortValue} within {testTimeoutSeconds}s.\n" +
                               "Verify the IP address and port are correct, the scale is powered on and listening, and no firewall is blocking the connection.";
                System.Windows.MessageBox.Show(
                    $"No response from {IpAddress}:{tcpPortValue} within {testTimeoutSeconds}s.\n\nVerify the scale is powered on, listening on this port, and reachable.",
                    "No Response", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return; // Exit early, don't show "no data" message
            }

            if (count == 0)
            {
                string modeHint = isSicsMode
                    ? "Check that the scale is powered on, set to SICS mode, and replies to SI commands."
                    : "Check that the scale is powered on and streaming data over TCP.";
                ReceivedData = $"Connected to {IpAddress}:{tcpPortValue} but no data received within {testTimeoutSeconds}s.\n" + modeHint;
                System.Windows.MessageBox.Show(
                    $"Connected to {IpAddress}:{tcpPortValue} but received no data.\n\n{modeHint}",
                    "No Data Received", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Test successful!\n\n{count} sample(s) received from {IpAddress}:{tcpPortValue}.",
                    "Connection Test", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        private static int IndexOfSettingsNewline(string s)
        {
            int r = s.IndexOf('\r');
            int n = s.IndexOf('\n');
            if (r < 0) return n;
            if (n < 0) return r;
            return Math.Min(r, n);
        }

        private async Task SaveScaleSettingsAsync()
        {
            try
            {
                // Validate Scale No
                if (!int.TryParse(ScaleCode, out int scaleCodeValue) || scaleCodeValue < 1 || scaleCodeValue > 9999)
                {
                    System.Windows.MessageBox.Show(
                        "Scale No is required and must be a number between 1 and 9999.",
                        "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // Validate TCP Port when TCP connection is selected
                int tcpPortValue = 5000;
                if (IsTcp)
                {
                    if (!int.TryParse(TcpPort, out tcpPortValue) || tcpPortValue < 1 || tcpPortValue > 65535)
                    {
                        System.Windows.MessageBox.Show(
                            "TCP Port is required and must be a number between 1 and 65535.",
                            "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                }

                // Validate capture interval when Auto capture is enabled
                if (IsAutoCapture)
                {
                    ValidateCaptureInterval();
                    if (!string.IsNullOrEmpty(CaptureIntervalError))
                    {
                        System.Windows.MessageBox.Show(
                            CaptureIntervalError,
                            "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                }

                // Validate zero return tolerance fields when ZeroReturn is enabled
                double toleranceMinusValue = 0;
                double tolerancePlusValue = 0;
                if (ZeroReturn)
                {
                    if (!double.TryParse(ZeroReturnToleranceMinus, out toleranceMinusValue) || toleranceMinusValue < 1 || toleranceMinusValue > 99)
                    {
                        System.Windows.MessageBox.Show(
                            "Tolerance - (minus) is required and must be between 1 and 99 when Zero Return is enabled.",
                            "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                    if (!double.TryParse(ZeroReturnTolerancePlus, out tolerancePlusValue) || tolerancePlusValue < 1 || tolerancePlusValue > 99)
                    {
                        System.Windows.MessageBox.Show(
                            "Tolerance + (plus) is required and must be between 1 and 99 when Zero Return is enabled.",
                            "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                }

                var s = new ScaleSettings
                {
                    Id = 1,
                    ScaleName = ScaleName ?? string.Empty,
                    Location = Location ?? string.Empty,
                    ScaleCode = scaleCodeValue,
                    ConnectionType = ConnectionType ?? "SerialPort",
                    PortName = IsSerialPort ? (SelectedPort ?? string.Empty) : string.Empty,
                    BaudRate = BaudRate,
                    DataBits = DataBits,
                    Parity = (int)Parity,
                    StopBits = (int)StopBits,
                    IpAddress = IsTcp ? IpAddress : string.Empty,
                    Port = IsTcp ? tcpPortValue : 0,
                    Mode = Mode ?? "Continuous",
                    ZeroReturn = ZeroReturn,
                    ZeroReturnToleranceMinus = ZeroReturn ? toleranceMinusValue : 0,
                    ZeroReturnTolerancePlus = ZeroReturn ? tolerancePlusValue : 0,
                    IsActive = true,
                    IsAutoCapture = IsAutoCapture,
                    CaptureIntervalMs = IsAutoCapture ? CaptureIntervalMs : 0,
                    CreatedAt = DateTime.Now
                };

                await _scaleConfigService.SaveSettingsAsync(s);

                // Reset the shared scale connection so Batch Weighing reconnects with the new settings.
                var dbPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ProWeighSuitePlatforms", "proweighsuiteplatform.db");
                var scaleService = ScaleReaderService.GetShared(new Data.ScaleSettingsRepository(dbPath));
                scaleService.StopReading();
                System.Diagnostics.Debug.WriteLine("[SettingsViewModel] Scale connection reset after settings save.");

                // Log settings change
                var username = _userContext.CurrentUser?.Username ?? "Unknown";
                await _auditService.LogSettingsChangedAsync(
                    username,
                    "Scale Settings",
                    null,
                    $"Port={s.PortName}, BaudRate={s.BaudRate}, ConnectionType={s.ConnectionType}, IP={s.IpAddress}, ScaleName={s.ScaleName}, Location={s.Location}, ZeroReturn={s.ZeroReturn}, ToleranceMinus={s.ZeroReturnToleranceMinus}, TolerancePlus={s.ZeroReturnTolerancePlus}, IsAutoCapture={s.IsAutoCapture}, CaptureIntervalMs={s.CaptureIntervalMs}");
                
                System.Windows.MessageBox.Show(
                    $"Scale settings saved.\nNavigate to Batch Weighing to reconnect with the new settings.",
                    "Settings Saved",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to save scale settings: {ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
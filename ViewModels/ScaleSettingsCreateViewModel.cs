using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class ScaleSettingsCreateViewModel : BaseViewModel
    {
        private readonly ScaleSettingsViewModel _parent;
        private readonly ScaleSettingsService _scaleService;
        private readonly ScaleSettings? _existingScale;

        public bool IsUpdating => _existingScale != null;

        public string Title => IsUpdating ? "Update Scale" : "Create Scale";

        public ObservableCollection<string> AvailablePorts { get; } = new();
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
                    ValidateConnectionSettings();
                }
            }
        }

        public bool IsSerialPort => ConnectionType == "SerialPort";
        public bool IsTcp => ConnectionType == "TCP";

        private string _createScaleName = string.Empty;
        public string CreateScaleName
        {
            get => _createScaleName;
            set
            {
                if (SetProperty(ref _createScaleName, value?.Trim() ?? string.Empty))
                {
                    ValidateScaleName();
                }
            }
        }

        // Serial Port fields
        private string? _selectedPort;
        public string? SelectedPort
        {
            get => _selectedPort;
            set
            {
                if (SetProperty(ref _selectedPort, value))
                {
                    ValidateConnectionSettings();
                }
            }
        }

        private int _baudRate = 9600;
        public int BaudRate
        {
            get => _baudRate;
            set => SetProperty(ref _baudRate, value);
        }

        private Parity _parity = Parity.None;
        public Parity Parity
        {
            get => _parity;
            set => SetProperty(ref _parity, value);
        }

        private int _dataBits = 8;
        public int DataBits
        {
            get => _dataBits;
            set => SetProperty(ref _dataBits, value);
        }

        private StopBits _stopBits = StopBits.One;
        public StopBits StopBits
        {
            get => _stopBits;
            set => SetProperty(ref _stopBits, value);
        }

        // TCP/IP fields
        private string _ipAddress = "192.168.1.100";
        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                if (SetProperty(ref _ipAddress, value?.Trim() ?? string.Empty))
                {
                    ValidateConnectionSettings();
                }
            }
        }

        private int _port = 5000;
        public int Port
        {
            get => _port;
            set
            {
                if (SetProperty(ref _port, value))
                {
                    ValidateConnectionSettings();
                }
            }
        }

        private string _mode = "Continuous";
        public string Mode
        {
            get => _mode;
            set
            {
                if (SetProperty(ref _mode, value))
                {
                    ValidateMode();
                }
            }
        }

        private bool _isActive = true;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

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

        private bool _zeroReturn = false;
        public bool ZeroReturn
        {
            get => _zeroReturn;
            set => SetProperty(ref _zeroReturn, value);
        }

        private double _zeroReturnToleranceMinus = 20;
        public double ZeroReturnToleranceMinus
        {
            get => _zeroReturnToleranceMinus;
            set => SetProperty(ref _zeroReturnToleranceMinus, value);
        }

        private double _zeroReturnTolerancePlus = 20;
        public double ZeroReturnTolerancePlus
        {
            get => _zeroReturnTolerancePlus;
            set => SetProperty(ref _zeroReturnTolerancePlus, value);
        }

        private string _receivedData = string.Empty;
        public string ReceivedData
        {
            get => _receivedData;
            set => SetProperty(ref _receivedData, value);
        }

        private string? _scaleNameError;
        public string? ScaleNameError
        {
            get => _scaleNameError;
            set => SetProperty(ref _scaleNameError, value);
        }

        private string? _connectionError;
        public string? ConnectionError
        {
            get => _connectionError;
            set => SetProperty(ref _connectionError, value);
        }

        private string? _modeError;
        public string? ModeError
        {
            get => _modeError;
            set => SetProperty(ref _modeError, value);
        }

        private int _scaleCode = 0;
        public int ScaleCode
        {
            get => _scaleCode;
            set
            {
                if (SetProperty(ref _scaleCode, value))
                    ValidateScaleCode();
            }
        }

        private string? _scaleCodeError;
        public string? ScaleCodeError
        {
            get => _scaleCodeError;
            set => SetProperty(ref _scaleCodeError, value);
        }

        private string _location = string.Empty;
        public string Location
        {
            get => _location;
            set => SetProperty(ref _location, value?.Trim() ?? string.Empty);
        }

        public ICommand RefreshPortsCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand CreateCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand CancelCreateCommand { get; }

        public ScaleSettingsCreateViewModel(ScaleSettingsViewModel parent, ScaleSettings? scale)
        {
            _parent = parent;
            _existingScale = scale;

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuiteFusion", "proweigh_suite_fusion.db");
            _scaleService = new ScaleSettingsService(new ScaleSettingsRepository(dbPath));

            // Initialize commands first
            RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
            TestConnectionCommand = new RelayCommand(async _ => 
            {
                try
                {
                    await TestConnectionAsync();
                }
                catch (Exception ex)
                {
                    // Catch any unhandled exceptions to prevent global error dialogs
                    ReceivedData = $"Test connection error: {ex.Message}\n\nPlease check your settings and try again.";
                    System.Diagnostics.Debug.WriteLine($"[ScaleTest] Unhandled exception in TestConnectionCommand: {ex.Message}");
                }
            }, _ => CanTest());
            CreateCommand = new RelayCommand(async _ => await CreateScaleAsync(), _ => CanCreate());
            UpdateCommand = new RelayCommand(async _ => await UpdateScaleAsync(), _ => CanCreate());
            CancelCreateCommand = new RelayCommand(async _ => await _parent.CancelCreate());

            // Load ports
            RefreshPorts();

            // Load existing scale data if updating
            if (scale != null)
            {
                CreateScaleName = scale.ScaleName;
                ConnectionType = scale.ConnectionType;
                SelectedPort = scale.PortName;
                BaudRate = scale.BaudRate;
                DataBits = scale.DataBits;
                Parity = (Parity)scale.Parity;
                StopBits = (StopBits)scale.StopBits;
                IpAddress = scale.IpAddress;
                Port = scale.Port;
                Mode = scale.Mode ?? "Continuous";
                ZeroReturn = scale.ZeroReturn;
                ZeroReturnToleranceMinus = scale.ZeroReturnToleranceMinus;
                ZeroReturnTolerancePlus = scale.ZeroReturnTolerancePlus;
                IsActive = scale.IsActive;
                ScaleCode = scale.ScaleCode;
                Location = scale.Location;
                IsAutoCapture = scale.IsAutoCapture;
                CaptureIntervalMs = scale.CaptureIntervalMs;
            }
        }

        // Parameterless constructor for design-time support

        private bool CanCreate()
        {
            return !string.IsNullOrWhiteSpace(CreateScaleName) &&
                   CreateScaleName.Length >= 2 &&
                   !string.IsNullOrWhiteSpace(Mode) &&
                   string.IsNullOrEmpty(ScaleNameError) &&
                   string.IsNullOrEmpty(ConnectionError) &&
                   string.IsNullOrEmpty(ModeError) &&
                   string.IsNullOrEmpty(ScaleCodeError) &&
                   string.IsNullOrEmpty(CaptureIntervalError) &&
                   ScaleCode >= 1 &&
                   (IsSerialPort ? !string.IsNullOrWhiteSpace(SelectedPort) : !string.IsNullOrWhiteSpace(IpAddress));
        }

        private bool CanTest()
        {
            return IsSerialPort ? !string.IsNullOrWhiteSpace(SelectedPort) : !string.IsNullOrWhiteSpace(IpAddress);
        }

        private void ValidateScaleName()
        {
            if (string.IsNullOrWhiteSpace(CreateScaleName))
            {
                ScaleNameError = "Scale name is required";
            }
            else if (CreateScaleName.Length < 2)
            {
                ScaleNameError = "Scale name must be at least 2 characters";
            }
            else if (CreateScaleName.Length > 100)
            {
                ScaleNameError = "Scale name must be less than 100 characters";
            }
            else
            {
                ScaleNameError = null;
            }
        }

        private void ValidateConnectionSettings()
        {
            if (IsSerialPort)
            {
                if (string.IsNullOrWhiteSpace(SelectedPort))
                {
                    ConnectionError = "Port is required";
                }
                else
                {
                    // Check if port is already in use by another scale
                    var excludeId = _existingScale?.Id;
                    var portExists = _scaleService.PortExistsAsync(SelectedPort, excludeId).Result;
                    
                    if (portExists)
                    {
                        ConnectionError = $"Port {SelectedPort} is already assigned to another scale";
                    }
                    else
                    {
                        ConnectionError = null;
                    }
                }
            }
            else // TCP
            {
                if (string.IsNullOrWhiteSpace(IpAddress))
                {
                    ConnectionError = "IP Address is required";
                }
                else if (!System.Net.IPAddress.TryParse(IpAddress, out _))
                {
                    ConnectionError = "Invalid IP Address format";
                }
                else if (Port < 1 || Port > 65535)
                {
                    ConnectionError = "Port must be between 1 and 65535";
                }
                else
                {
                    ConnectionError = null;
                }
            }
        }

        private void ValidateMode()
        {
            if (string.IsNullOrWhiteSpace(Mode))
            {
                ModeError = "Mode is required";
            }
            else if (Mode != "Continuous" && Mode != "SICS")
            {
                ModeError = "Mode must be either 'Continuous' or 'SICS'";
            }
            else
            {
                ModeError = null;
            }
        }

        private void ValidateCaptureInterval()
        {
            if (IsAutoCapture)
            {
                if (CaptureIntervalMs < 100)
                    CaptureIntervalError = "Must be at least 100 ms";
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

        private void ValidateScaleCode()
        {
            if (ScaleCode < 1 || ScaleCode > 9999)
                ScaleCodeError = "Scale Code must be between 1 and 9999";
            else
                ScaleCodeError = null;

            CommandManager.InvalidateRequerySuggested();
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
            ReceivedData = IsSerialPort
                ? $"Connecting to {SelectedPort} at {BaudRate} baud..."
                : $"Connecting to {IpAddress}:{Port}...";
            try
            {
                if (IsSerialPort)
                    await TestSerialPortConnectionAsync();
                else
                    await TestTcpConnectionAsync();
            }
            catch (OperationCanceledException)
            {
                // Timeout is already handled in the inner methods with user-friendly messages
                // This is a normal scenario, not an error - just ignore it here
                System.Diagnostics.Debug.WriteLine("[ScaleTest] Test operation timed out (handled by inner method)");
            }
            catch (Exception ex)
            {
                // Only catch unexpected errors (not timeout/cancellation)
                ReceivedData = $"Connection failed: {ex.Message}\n\nPlease verify connection settings and try again.";
                System.Diagnostics.Debug.WriteLine($"[ScaleTest] Connection error: {ex.Message}");
            }
        }

        private async Task TestSerialPortConnectionAsync()
        {
            const int targetSamples = 10;
            const int testTimeoutSeconds = 10;

            var sb = new StringBuilder();
            int count = 0;
            bool isSicsMode = Mode == "SICS";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(testTimeoutSeconds));

            try
            {
                await Task.Run(() =>
                {
                    using var port = new SerialPort(SelectedPort, BaudRate, Parity, DataBits, StopBits)
                    {
                        ReadTimeout  = 2000,
                        WriteTimeout = 1000,
                        NewLine      = "\r\n"
                    };

                    port.Open();

                    if (isSicsMode)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            ReceivedData = $"Connected to {SelectedPort} (SICS Mode). Sending SIR commands to request weight (timeout {testTimeoutSeconds}s)...\n\n");
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            ReceivedData = $"Connected to {SelectedPort} (Continuous Mode). Listening for weight data (timeout {testTimeoutSeconds}s)...\n\n");
                    }

                    while (count < targetSamples && !cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            if (isSicsMode)
                            {
                                // SICS Mode: Send SI command to request weight
                                port.WriteLine("SI");
                                System.Diagnostics.Debug.WriteLine($"[ScaleTest] Sent SIR command: SI to {SelectedPort}");
                            }

                            string line = port.ReadLine();
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                sb.AppendLine(line.Trim());
                                count++;

                                // Append new data to the existing connection message
                                var modeMsg = isSicsMode ? "(SICS Mode)" : "(Continuous Mode)";
                                var snapshot = $"Connected to {SelectedPort} {modeMsg}. Received {count} sample(s)...\n\n" + sb.ToString();
                                Application.Current.Dispatcher.Invoke(() => ReceivedData = snapshot);
                                System.Diagnostics.Debug.WriteLine($"[ScaleTest] Received: {line.Trim()}");
                            }

                            // For SICS mode, add a small delay between commands
                            if (isSicsMode && count < targetSamples)
                            {
                                System.Threading.Thread.Sleep(500); // 500ms between requests
                            }
                        }
                        catch (TimeoutException)
                        {
                            if (isSicsMode)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ScaleTest] No response to SIR command (timeout)");
                            }
                            // no data yet � keep waiting
                        }
                    }

                    port.Close();
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout occurred - this is normal if scale doesn't send data
                // TaskCanceledException inherits from OperationCanceledException
                System.Diagnostics.Debug.WriteLine($"[ScaleTest] Serial port test timed out after {testTimeoutSeconds}s waiting for data");
            }
            catch (UnauthorizedAccessException)
            {
                ReceivedData = $"Port {SelectedPort} is already in use by another application.\n\nPlease close any other programs using this port and try again.";
                System.Diagnostics.Debug.WriteLine($"[ScaleTest] Port access denied: {SelectedPort}");
                return; // Exit early, don't show "no data" message
            }
            catch (IOException ex)
            {
                ReceivedData = $"Cannot access port {SelectedPort}.\n\n{ex.Message}\n\nVerify the port exists and is not in use.";
                System.Diagnostics.Debug.WriteLine($"[ScaleTest] IO error: {ex.Message}");
                return; // Exit early
            }
            catch (Exception ex)
            {
                ReceivedData = $"Error testing connection to {SelectedPort}:\n\n{ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[ScaleTest] Unexpected error: {ex.Message}");
                return; // Exit early
            }

            if (count == 0)
            {
                string modeInfo = isSicsMode ? " (SICS mode - sent SIR commands)" : " (Continuous mode)";
                string scaleMode = isSicsMode ? "SICS" : "Continuous";
                ReceivedData = $"Connected to {SelectedPort}{modeInfo}.\n\n" +
                               $"No data received within {testTimeoutSeconds}s.\n\n" +
                               $"Verify:\n" +
                               $"- Scale baud rate and protocol settings match\n" +
                               $"- Scale is set to {scaleMode} mode\n" +
                               $"- Scale is powered on and functioning";
            }
            else
            {
                // Data is already displayed in ReceivedData - just add summary at the end
                ReceivedData += $"\n? Test successful! {count} sample(s) received from {SelectedPort}.";
            }
        }

        private async Task TestTcpConnectionAsync()
        {
            const int targetSamples = 10;
            const int testTimeoutSeconds = 10;

            var sb = new StringBuilder();
            int count = 0;
            bool isSicsMode = Mode == "SICS";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(testTimeoutSeconds));

            try
            {
                await Task.Run(async () =>
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(IpAddress, Port, cts.Token);

                    if (isSicsMode)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            ReceivedData = $"Connected to {IpAddress}:{Port} (SICS Mode). Sending SIR commands to request weight (timeout {testTimeoutSeconds}s)...\n\n");
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            ReceivedData = $"Connected to {IpAddress}:{Port} (Continuous Mode). Listening for weight data (timeout {testTimeoutSeconds}s)...\n\n");
                    }

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
                                System.Diagnostics.Debug.WriteLine($"[ScaleTest] Sent SIR command: SI\\r\\n to {IpAddress}:{Port}");

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
                                    while ((nl = IndexOfNewline(raw)) >= 0 && count < targetSamples)
                                    {
                                        var line = raw[..nl].Trim();
                                        raw = raw[(nl + 1)..].TrimStart('\n');
                                        if (!string.IsNullOrWhiteSpace(line))
                                        {
                                            sb.AppendLine(line);
                                            count++;

                                            // Append new data to the existing connection message
                                            var modeMsg = isSicsMode ? "(SICS Mode)" : "(Continuous Mode)";
                                            var snapshot = $"Connected to {IpAddress}:{Port} {modeMsg}. Received {count} sample(s)...\n\n" + sb.ToString();
                                            Application.Current.Dispatcher.Invoke(() => ReceivedData = snapshot);
                                            System.Diagnostics.Debug.WriteLine($"[ScaleTest] Received: {line}");
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
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ScaleTest] Read error: {ex.Message}");
                            // transient read error � keep trying until timeout
                        }
                    }
                        }, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout occurred - this is normal if scale doesn't send data
                        // TaskCanceledException inherits from OperationCanceledException
                        System.Diagnostics.Debug.WriteLine($"[ScaleTest] TCP test timed out after {testTimeoutSeconds}s waiting for data");
                    }
                    catch (SocketException ex)
                    {
                        ReceivedData = $"Cannot connect to {IpAddress}:{Port}.\n\n{ex.Message}\n\nVerify the IP address and port are correct and the scale is powered on.";
                        System.Diagnostics.Debug.WriteLine($"[ScaleTest] Socket error: {ex.Message}");
                        return; // Exit early, don't show "no data" message
                    }
                    catch (IOException ex)
                    {
                        ReceivedData = $"Communication error with {IpAddress}:{Port}.\n\n{ex.Message}";
                        System.Diagnostics.Debug.WriteLine($"[ScaleTest] IO error: {ex.Message}");
                        return; // Exit early
                    }
                    catch (Exception ex)
                    {
                        ReceivedData = $"Error testing connection to {IpAddress}:{Port}:\n\n{ex.Message}";
                        System.Diagnostics.Debug.WriteLine($"[ScaleTest] Unexpected error: {ex.Message}");
                        return; // Exit early
                    }

                    if (count == 0)
            {
                string modeInfo = isSicsMode ? " (SICS mode - sent SIR commands)" : " (Continuous mode)";
                string scaleMode = isSicsMode ? "SICS" : "Continuous";
                ReceivedData = $"Connected to {IpAddress}:{Port}{modeInfo}.\n\n" +
                               $"No data received within {testTimeoutSeconds}s.\n\n" +
                               $"Verify:\n" +
                               $"- Scale is set to {scaleMode} mode\n" +
                               $"- Scale is configured for TCP/IP communication\n" +
                               $"- Scale is powered on and functioning";
            }
            else
            {
                // Data is already displayed in ReceivedData - just add summary at the end
                ReceivedData += $"\n? Test successful! {count} sample(s) received from {IpAddress}:{Port}.";
            }
        }

        private static int IndexOfNewline(string s)
        {
            int r = s.IndexOf('\r');
            int n = s.IndexOf('\n');
            if (r < 0) return n;
            if (n < 0) return r;
            return Math.Min(r, n);
        }

        private async Task CreateScaleAsync()
        {
            try
            {
                // Final validation before saving
                ValidateScaleName();
                ValidateConnectionSettings();
                ValidateMode();
                ValidateScaleCode();

                if (!string.IsNullOrEmpty(ScaleNameError) || !string.IsNullOrEmpty(ConnectionError) || !string.IsNullOrEmpty(ModeError) || !string.IsNullOrEmpty(ScaleCodeError) || !string.IsNullOrEmpty(CaptureIntervalError))
                {
                    MessageBox.Show("Please fix validation errors before saving.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Double-check port uniqueness for serial connections
                if (IsSerialPort)
                {
                    var portExists = await _scaleService.PortExistsAsync(SelectedPort ?? string.Empty, null);
                    if (portExists)
                    {
                        ConnectionError = $"Port {SelectedPort} is already assigned to another scale";
                        MessageBox.Show($"Port {SelectedPort} is already in use by another scale. Please select a different port.", 
                            "Duplicate Port", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                var scale = new ScaleSettings
                {
                    ScaleName = CreateScaleName.Trim(),
                    ConnectionType = ConnectionType,
                    PortName = IsSerialPort ? (SelectedPort ?? string.Empty) : string.Empty,
                    BaudRate = BaudRate,
                    DataBits = DataBits,
                    Parity = (int)Parity,
                    StopBits = (int)StopBits,
                    IpAddress = IsTcp ? IpAddress : string.Empty,
                    Port = IsTcp ? Port : 0,
                    Mode = Mode,
                    ZeroReturn = ZeroReturn,
                    ZeroReturnToleranceMinus = ZeroReturn ? ZeroReturnToleranceMinus : 0,
                    ZeroReturnTolerancePlus = ZeroReturn ? ZeroReturnTolerancePlus : 0,
                    IsActive = IsActive,
                    ScaleCode = ScaleCode,
                    Location = Location,
                    IsAutoCapture = IsAutoCapture,
                    CaptureIntervalMs = IsAutoCapture ? CaptureIntervalMs : 0,
                    CreatedAt = DateTime.Now
                };

                await _scaleService.AddAsync(scale);
                
                // Force command manager to requery all commands
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                
                MessageBox.Show("Scale created successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                await _parent.CancelCreate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating scale: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateScaleAsync()
        {
            if (_existingScale == null) return;

            try
            {
                // Final validation before updating
                ValidateScaleName();
                ValidateConnectionSettings();
                ValidateMode();
                ValidateScaleCode();

                if (!string.IsNullOrEmpty(ScaleNameError) || !string.IsNullOrEmpty(ConnectionError) || !string.IsNullOrEmpty(ModeError) || !string.IsNullOrEmpty(ScaleCodeError) || !string.IsNullOrEmpty(CaptureIntervalError))
                {
                    MessageBox.Show("Please fix validation errors before updating.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Double-check port uniqueness for serial connections (excluding current scale)
                if (IsSerialPort)
                {
                    var portExists = await _scaleService.PortExistsAsync(SelectedPort ?? string.Empty, _existingScale.Id);
                    if (portExists)
                    {
                        ConnectionError = $"Port {SelectedPort} is already assigned to another scale";
                        MessageBox.Show($"Port {SelectedPort} is already in use by another scale. Please select a different port.", 
                            "Duplicate Port", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                var scale = new ScaleSettings
                {
                    Id = _existingScale.Id,
                    ScaleName = CreateScaleName.Trim(),
                    ConnectionType = ConnectionType,
                    PortName = IsSerialPort ? (SelectedPort ?? string.Empty) : string.Empty,
                    BaudRate = BaudRate,
                    DataBits = DataBits,
                    Parity = (int)Parity,
                    StopBits = (int)StopBits,
                    IpAddress = IsTcp ? IpAddress : string.Empty,
                    Port = IsTcp ? Port : 0,
                    Mode = Mode,
                    ZeroReturn = ZeroReturn,
                    ZeroReturnToleranceMinus = ZeroReturn ? ZeroReturnToleranceMinus : 0,
                    ZeroReturnTolerancePlus = ZeroReturn ? ZeroReturnTolerancePlus : 0,
                    IsActive = IsActive,
                    ScaleCode = ScaleCode,
                    Location = Location,
                    IsAutoCapture = IsAutoCapture,
                    CaptureIntervalMs = IsAutoCapture ? CaptureIntervalMs : 0,
                    CreatedAt = _existingScale.CreatedAt
                };

                await _scaleService.UpdateAsync(scale);
                // Force command manager to requery all commands
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                
                MessageBox.Show("Scale updated successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                await _parent.CancelCreate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating scale: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

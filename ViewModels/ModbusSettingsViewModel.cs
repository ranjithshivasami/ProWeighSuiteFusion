using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Security;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Views;

namespace ProWeighSuitePlatforms.ViewModels
{
    /// <summary>
    /// ViewModel for the PLC Settings workspace. Edits the Modbus RTU configuration
    /// persisted by ConfigManager (plc-config.json), provides a serial link test and a
    /// live manual I/O diagnostic console driven by <see cref="ModbusRtuClient"/>.
    ///
    /// Every manual I/O routine validates inputs BEFORE any bytes reach the wire and
    /// funnels runtime faults into a layered catch ladder (timeout -> protocol exception
    /// -> general) so technicians always see actionable text in <see cref="StatusLogMessage"/>.
    /// </summary>
    public class ModbusSettingsViewModel : BaseViewModel
    {
        public string Title => "PLC Settings";

        private readonly ConfigManager _configManager = new();
        private readonly AuditService _auditService = new();
        private ModbusRtuClient? _modbusClient;

        // ---- On-demand listen for inputs 0-7 (button triggered, FC02) -------------------------
        private CancellationTokenSource? _inputPollCts;
        private Task? _inputPollTask;
        private readonly bool[] _lastInputStates = new bool[8];
        private bool _inputPollFirst = true;

        private bool _isListening;
        public bool IsListening
        {
            get => _isListening;
            private set
            {
                if (SetProperty(ref _isListening, value))
                {
                    RaisePropertyChanged(nameof(ListeningButtonText));
                    RaisePropertyChanged(nameof(ListeningStatusText));
                }
            }
        }

        public string ListeningButtonText => IsListening ? "Stop Listening" : "Listen Inputs 0-7";
        public string ListeningStatusText => IsListening ? "● Listening inputs 0-7..." : "Idle - press Listen to poll inputs 0-7";
        public bool CanToggleListening => IsConnected && !IsBusy;

        // ---- Option lists for the combo boxes ---------------------------------

        public ObservableCollection<string> AvailablePorts { get; } = new();

        public int[] BaudRateOptions { get; } = { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };

        public int[] DataBitsOptions { get; } = { 7, 8 };

        // StopBits.None is not valid for an RTU line - never offer it.
        public StopBits[] StopBitsOptions { get; } = { StopBits.One, StopBits.Two };

        // Mark/Space parity is rarely supported by Modbus RTU slaves - offer the standard trio.
        public Parity[] ParityOptions { get; } = { Parity.None, Parity.Odd, Parity.Even };

        // ---- Serial port parameters --------------------------------------------

        private string _selectedPortName = string.Empty;
        public string SelectedPortName
        {
            get => _selectedPortName;
            set => SetProperty(ref _selectedPortName, value?.Trim() ?? string.Empty);
        }

        private int _selectedBaudRate = 9600;
        public int SelectedBaudRate
        {
            get => _selectedBaudRate;
            set => SetProperty(ref _selectedBaudRate, value);
        }

        private int _selectedDataBits = 8;
        public int SelectedDataBits
        {
            get => _selectedDataBits;
            set => SetProperty(ref _selectedDataBits, value);
        }

        private StopBits _selectedStopBits = StopBits.One;
        public StopBits SelectedStopBits
        {
            get => _selectedStopBits;
            set => SetProperty(ref _selectedStopBits, value);
        }

        private Parity _selectedParity = Parity.None;
        public Parity SelectedParity
        {
            get => _selectedParity;
            set => SetProperty(ref _selectedParity, value);
        }

        private string _slaveAddressText = "1";
        public string SlaveAddressText
        {
            get => _slaveAddressText;
            set => SetProperty(ref _slaveAddressText, value?.Trim() ?? string.Empty);
        }

        // ---- Hardware register mapping ------------------------------------------

        private string _itemArrivedSensorAddressText = "0";
        public string ItemArrivedSensorAddressText
        {
            get => _itemArrivedSensorAddressText;
            set => SetProperty(ref _itemArrivedSensorAddressText, value?.Trim() ?? string.Empty);
        }

        private string _underWeightBuzzerAddressText = "0";
        public string UnderWeightBuzzerAddressText
        {
            get => _underWeightBuzzerAddressText;
            set => SetProperty(ref _underWeightBuzzerAddressText, value?.Trim() ?? string.Empty);
        }

        private string _overWeightBuzzerAddressText = "1";
        public string OverWeightBuzzerAddressText
        {
            get => _overWeightBuzzerAddressText;
            set => SetProperty(ref _overWeightBuzzerAddressText, value?.Trim() ?? string.Empty);
        }

        private string _acceptSignalAddressText = "2";
        public string AcceptSignalAddressText
        {
            get => _acceptSignalAddressText;
            set => SetProperty(ref _acceptSignalAddressText, value?.Trim() ?? string.Empty);
        }

        // ---- Holding Register Mapping (mirrors mapping.html) ------------------------
        public ObservableCollection<HoldingRegisterMapRow> HoldingRegisterMaps { get; } = new();
        // Short keys for storage; Display text stays "Idle/ Reset" etc. — XAML shows Display via WpfDefineOptions/Display path
        public PlcConfig.WpfDefineOption[] WpfDefineOptions => PlcConfig.WpfDefineOptionList;
        // Backward compat helper if anything binds to string keys
        public string[] WpfDefineKeys => PlcConfig.WpfDefineKeys;

        public ICommand AddHoldingMapRowCommand { get; }
        public ICommand DeleteHoldingMapRowCommand { get; }
        public ICommand ExportHoldingMapsCommand { get; }

        // ---- Discrete Input Mapping (Address | Value | WPF Define = weighingInProcess|capture) ----
        public ObservableCollection<DiscreteInputMapRow> DiscreteInputMaps { get; } = new();
        public PlcConfig.WpfDefineOption[] DiscreteWpfOptions => PlcConfig.DiscreteWpfOptionList;

        public ICommand AddDiscreteInputMapRowCommand { get; }
        public ICommand DeleteDiscreteInputMapRowCommand { get; }

        // ---- Live I/O diagnostics -------------------------------------------------

        private ushort _manualWriteAddress = 0; // Refactored: Poll proves 0 is writable (01 06 00 00 00 01 48 0A)
        /// <summary>
        /// Target address for the manual register write (FC06). Defaults to 0 (D0) as proven by ModbusPoll @0.
        /// Non-numeric keystrokes are rejected by the WPF binder.
        /// </summary>
        public ushort ManualWriteAddress
        {
            get => _manualWriteAddress;
            set => SetProperty(ref _manualWriteAddress, value);
        }

        private string _manualWriteValue = "0";
        /// <summary>Payload for FC06 holding-register writes: 0-65535 decimal or 0x hex; binary "1"/"0"/"true"/"false" is accepted as 1/0.</summary>
        public string ManualWriteValue
        {
            get => _manualWriteValue;
            set => SetProperty(ref _manualWriteValue, value?.Trim() ?? string.Empty);
        }

        private ushort _manualReadAddress;
        /// <summary>Source address for the manual read.</summary>
        public ushort ManualReadAddress
        {
            get => _manualReadAddress;
            set => SetProperty(ref _manualReadAddress, value);
        }

        private string _diagnosticDisplayResult = string.Empty;
        /// <summary>Compact summary of the most recent completed operation, shown above the log console.</summary>
        public string DiagnosticDisplayResult
        {
            get => _diagnosticDisplayResult;
            private set => SetProperty(ref _diagnosticDisplayResult, value);
        }

        private string _statusLogMessage = string.Empty;
        /// <summary>
        /// Timestamped technician-facing event log ([Error], [Validation Error],
        /// [Hardware Error], [Protocol Error], plus successes). Bound to the diagnostics console.
        /// </summary>
        public string StatusLogMessage
        {
            get => _statusLogMessage;
            private set => SetProperty(ref _statusLogMessage, value);
        }

        // ---- Status / busy / connection state --------------------------------------

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    RaisePropertyChanged(nameof(ConnectionStatusText));
                    RaisePropertyChanged(nameof(CanExecuteDiagnostics));
                    RaisePropertyChanged(nameof(CanToggleListening));
                }
            }
        }

        public string ConnectionStatusText => IsConnected
            ? $"Connected - {_selectedPortName} @ {_selectedBaudRate} baud"
            : "Offline";

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private bool _isStatusError;
        public bool IsStatusError
        {
            get => _isStatusError;
            private set => SetProperty(ref _isStatusError, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaisePropertyChanged(nameof(CanExecuteActions));
                    RaisePropertyChanged(nameof(CanExecuteDiagnostics));
                    RaisePropertyChanged(nameof(CanToggleListening));
                }
            }
        }

        public bool CanExecuteActions => !IsBusy;
        public bool CanExecuteDiagnostics => IsConnected && !IsBusy;

        // ---- Logging / parsing internals ---------------------------------------------

        private const int MaxStatusLogChars = 12000;
        private readonly StringBuilder _statusLog = new();

        private static readonly string[] BinaryTrueTokens = { "1", "true" };
        private static readonly string[] BinaryFalseTokens = { "0", "false" };

        public ICommand TestConnectionCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand RefreshPortsCommand { get; }
        public ICommand ToggleConnectionCommand { get; }
        public ICommand SendManualWriteCommand { get; }
        public ICommand ReceiveManualReadCommand { get; }
        public ICommand ToggleListeningCommand { get; }

        private const int MaxHoldingMapRows = 6;
        private const int MaxDiscreteMapRows = 7;

        public bool CanAddHoldingMapRow => HoldingRegisterMaps.Count < MaxHoldingMapRows;
        public bool CanAddDiscreteInputMapRow => DiscreteInputMaps.Count < MaxDiscreteMapRows;

        public ModbusSettingsViewModel()
        {
            TestConnectionCommand = new RelayCommand(async _ => await TestConnectionAsync(), _ => CanExecuteActions);
            SaveSettingsCommand = new RelayCommand(async _ => await SaveSettingsAsync(), _ => CanExecuteActions);
            RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
            ToggleConnectionCommand = new RelayCommand(async _ => await ToggleConnectionAsync(), _ => CanExecuteActions);
            SendManualWriteCommand = new RelayCommand(async _ => await ExecuteManualWriteCommandAsync(), _ => CanExecuteDiagnostics);
            ReceiveManualReadCommand = new RelayCommand(async _ => await ExecuteManualReadCommandAsync(), _ => CanExecuteDiagnostics);
            ToggleListeningCommand = new RelayCommand(async _ => await ToggleListeningAsync(), _ => CanToggleListening);
            AddHoldingMapRowCommand = new RelayCommand(_ => AddHoldingMapRow(), _ => CanAddHoldingMapRow);
            DeleteHoldingMapRowCommand = new RelayCommand(async p => { if (p is HoldingRegisterMapRow r) await DeleteHoldingMapRowAsync(r); });
            ExportHoldingMapsCommand = new RelayCommand(_ => ExportHoldingMaps());
            AddDiscreteInputMapRowCommand = new RelayCommand(_ => AddDiscreteInputMapRow(), _ => CanAddDiscreteInputMapRow);
            DeleteDiscreteInputMapRowCommand = new RelayCommand(async p => { if (p is DiscreteInputMapRow r) await DeleteDiscreteInputMapRowAsync(r); });

            // Seed with defaults until config loads
            foreach (var d in PlcConfig.GetDefaultHoldingRegisterMaps())
                HoldingRegisterMaps.Add(new HoldingRegisterMapRow { Address = d.Address, Value = d.Value, WpfDefine = d.WpfDefine });
            foreach (var d in PlcConfig.GetDefaultDiscreteInputMaps())
                DiscreteInputMaps.Add(new DiscreteInputMapRow { Address = d.Address, Value = d.Value, WpfDefine = d.WpfDefine, InputName = d.InputName, CutOffTimeMs = d.CutOffTimeMs });

            HoldingRegisterMaps.CollectionChanged += (_, __) =>
            {
                RaisePropertyChanged(nameof(CanAddHoldingMapRow));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            };
            DiscreteInputMaps.CollectionChanged += (_, __) =>
            {
                RaisePropertyChanged(nameof(CanAddDiscreteInputMapRow));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            };

            RefreshPorts();
            _ = LoadAsync();
        }

        private void AddHoldingMapRow()
        {
            if (HoldingRegisterMaps.Count >= MaxHoldingMapRows)
            {
                LogError($"[Validation Error] Maximum {MaxHoldingMapRows} rows allowed — delete a row before adding.");
                return;
            }
            // All fields required — validate existing rows are complete before allowing a new one
            for (int i = 0; i < HoldingRegisterMaps.Count; i++)
            {
                var r = HoldingRegisterMaps[i];
                if (string.IsNullOrWhiteSpace(r.WpfDefine))
                {
                    LogError($"[Validation Error] Holding row {i + 1}: WPF Define is required — select a value before adding a new row.");
                    return;
                }
            }
            ushort nextValue = 0;
            if (HoldingRegisterMaps.Count > 0)
            {
                var last = HoldingRegisterMaps[^1].Value;
                if (last < ushort.MaxValue) nextValue = (ushort)(last + 1);
            }
            HoldingRegisterMaps.Add(new HoldingRegisterMapRow { Address = 0, Value = nextValue, WpfDefine = string.Empty });
        }

        private void AddDiscreteInputMapRow()
        {
            if (DiscreteInputMaps.Count >= MaxDiscreteMapRows)
            {
                LogError($"[Validation Error] Maximum {MaxDiscreteMapRows} discrete rows allowed — delete a row before adding.");
                return;
            }
            // All fields required — validate existing rows are complete before allowing a new one
            for (int i = 0; i < DiscreteInputMaps.Count; i++)
            {
                var r = DiscreteInputMaps[i];
                if (string.IsNullOrWhiteSpace(r.WpfDefine))
                {
                    LogError($"[Validation Error] Row {i + 1}: Function defenation is required — select a value before adding a new row.");
                    return;
                }
                var name = (r.InputName ?? string.Empty).Trim();
                if (name.Length < 3 || name.Length > 50)
                {
                    LogError($"[Validation Error] Row {i + 1}: Input Name is required (3-50 chars) — fix before adding a new row.");
                    return;
                }
                // CutOffTimeMs is numeric-only via behavior; 0 is allowed as valid time, but empty is not — already coerced to 0
            }
            ushort nextValue = 0;
            if (DiscreteInputMaps.Count > 0)
            {
                var last = DiscreteInputMaps[^1].Value;
                if (last < ushort.MaxValue) nextValue = (ushort)(last + 1);
            }
            DiscreteInputMaps.Add(new DiscreteInputMapRow { Address = 0, Value = nextValue, WpfDefine = string.Empty, InputName = string.Empty, CutOffTimeMs = 50 });
        }

        private async Task DeleteHoldingMapRowAsync(HoldingRegisterMapRow row)
        {
            var owner = Application.Current?.MainWindow;
            var username = GetCurrentUsername();
            var confirmed = await PasswordPromptWindow.ConfirmPasswordAsync(owner ?? new Window(), username);
            if (!confirmed)
            {
                MessageBox.Show("Password incorrect or cancelled.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (HoldingRegisterMaps.Contains(row))
                HoldingRegisterMaps.Remove(row);
        }

        private async Task DeleteDiscreteInputMapRowAsync(DiscreteInputMapRow row)
        {
            var owner = Application.Current?.MainWindow;
            var username = GetCurrentUsername();
            var confirmed = await PasswordPromptWindow.ConfirmPasswordAsync(owner ?? new Window(), username);
            if (!confirmed)
            {
                MessageBox.Show("Password incorrect or cancelled.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DiscreteInputMaps.Contains(row))
                DiscreteInputMaps.Remove(row);
        }

        private string GetCurrentUsername()
        {
            try
            {
                if (Application.Current?.MainWindow?.DataContext is ICurrentUserContext main)
                    return main.CurrentUser?.Username ?? "Unknown";
            }
            catch { }
            return "Unknown";
        }

        private void ExportHoldingMaps()
        {
            try
            {
                var data = HoldingRegisterMaps.Select(r => new { address = r.Address, value = r.Value, wpfDefine = r.WpfDefine }).ToArray();
                var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                try { System.Windows.Clipboard.SetText(json); } catch { }
                LogInfo($"Holding Register Maps exported ({data.Length} rows) — also copied to clipboard:\n{json}");
                System.Windows.MessageBox.Show(json, "Holding Register Maps — Export JSON", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogError($"[Error] Export failed: {ex.Message}");
            }
        }

        // ---- Configuration load / save ------------------------------------------------

        private async Task LoadAsync()
        {
            try
            {
                var config = await _configManager.LoadPlcConfigAsync();

                SelectedPortName = config.PortName;
                SelectedBaudRate = config.BaudRate;
                SelectedDataBits = config.DataBits;
                SelectedStopBits = config.StopBits;
                SelectedParity = config.Parity;
                SlaveAddressText = config.SlaveAddress.ToString();
                ItemArrivedSensorAddressText = config.ItemArrivedSensorAddress.ToString();
                UnderWeightBuzzerAddressText = config.UnderWeightBuzzerAddress.ToString();
                OverWeightBuzzerAddressText = config.OverWeightBuzzerAddress.ToString();
                AcceptSignalAddressText = config.AcceptSignalAddress.ToString();

                var maps = config.HoldingRegisterMaps ?? PlcConfig.GetDefaultHoldingRegisterMaps();
                HoldingRegisterMaps.Clear();
                foreach (var m in maps)
                    HoldingRegisterMaps.Add(new HoldingRegisterMapRow { Address = m.Address, Value = m.Value, WpfDefine = PlcConfig.NormalizeWpfDefine(m.WpfDefine) });

                var dMaps = config.DiscreteInputMaps ?? PlcConfig.GetDefaultDiscreteInputMaps();
                DiscreteInputMaps.Clear();
                foreach (var m in dMaps)
                    DiscreteInputMaps.Add(new DiscreteInputMapRow { Address = m.Address, Value = m.Value, WpfDefine = PlcConfig.NormalizeDiscreteWpfDefine(m.WpfDefine), InputName = m.InputName ?? string.Empty, CutOffTimeMs = m.CutOffTimeMs });

                LogInfo($"Configuration loaded (saved {config.LastModified:dd-MM-yyyy HH:mm}).");
            }
            catch (Exception ex)
            {
                LogError($"[Error] Failed to load plc-config.json: {ex.Message}");
            }
        }

        private void RefreshPorts()
        {
            var selected = SelectedPortName;
            AvailablePorts.Clear();
            try
            {
                foreach (var port in SerialPort.GetPortNames().OrderBy(p => p))
                    AvailablePorts.Add(port);
            }
            catch
            {
                // SerialPort.GetPortNames can throw on some drivers - leave list empty.
            }

            if (AvailablePorts.Count == 0 && string.IsNullOrEmpty(selected))
                return;

            // Keep the configured port visible even when the adapter is unplugged,
            // so the user can still see/save what was configured.
            if (!string.IsNullOrEmpty(selected) && !AvailablePorts.Contains(selected))
                AvailablePorts.Insert(0, selected);

            if (string.IsNullOrEmpty(SelectedPortName) && AvailablePorts.Count > 0)
                SelectedPortName = AvailablePorts[0];
        }

        // ---- Model mapping & validation --------------------------------------------------

        /// <summary>Builds a PlcConfig from the current editor fields.</summary>
        private PlcConfig BuildConfig() => new()
        {
            PortName = SelectedPortName,
            BaudRate = SelectedBaudRate,
            DataBits = SelectedDataBits,
            StopBits = SelectedStopBits,
            Parity = SelectedParity,
            SlaveAddress = byte.TryParse(SlaveAddressText, out var slave) ? slave : (byte)0,
            ItemArrivedSensorAddress = ushort.TryParse(ItemArrivedSensorAddressText, out var sensor) ? sensor : ushort.MaxValue,
            UnderWeightBuzzerAddress = ushort.TryParse(UnderWeightBuzzerAddressText, out var under) ? under : ushort.MaxValue,
            OverWeightBuzzerAddress = ushort.TryParse(OverWeightBuzzerAddressText, out var over) ? over : ushort.MaxValue,
            AcceptSignalAddress = ushort.TryParse(AcceptSignalAddressText, out var accept) ? accept : ushort.MaxValue,
            HoldingRegisterMaps = HoldingRegisterMaps.Select(r => new HoldingRegisterMapEntry { Address = r.Address, Value = r.Value, WpfDefine = PlcConfig.NormalizeWpfDefine(r.WpfDefine) }).ToList(),
            DiscreteInputMaps = DiscreteInputMaps.Select(r => new DiscreteInputMapEntry { Address = r.Address, Value = r.Value, WpfDefine = PlcConfig.NormalizeDiscreteWpfDefine(r.WpfDefine), InputName = (r.InputName ?? string.Empty).Trim(), CutOffTimeMs = r.CutOffTimeMs }).ToList()
        };

        /// <summary>Model-level editor validation used before Connect / Save / Test.</summary>
        private string[] ValidateAll()
        {
            var errors = new System.Collections.Generic.List<string>();

            if (!byte.TryParse(SlaveAddressText, out var slave) || slave < 1 || slave > 247)
                errors.Add("Slave address must be a number between 1 and 247.");

            foreach (var (label, text) in new[]
            {
                ("Item Arrived Sensor", ItemArrivedSensorAddressText),
                ("Under Weight Buzzer", UnderWeightBuzzerAddressText),
                ("Over Weight Buzzer", OverWeightBuzzerAddressText),
                ("Accept Signal", AcceptSignalAddressText)
            })
            {
                if (!ushort.TryParse(text, out _))
                    errors.Add($"{label} address must be a whole number between 0 and 65535.");
            }

            errors.AddRange(BuildConfig().Validate());

            // Discrete input mapping validation: all fields required
            for (int i = 0; i < DiscreteInputMaps.Count; i++)
            {
                var row = DiscreteInputMaps[i];
                if (string.IsNullOrWhiteSpace(row.WpfDefine))
                    errors.Add($"Discrete Input row {i + 1}: Function defenation is required.");
                var name = (row.InputName ?? string.Empty).Trim();
                if (name.Length < 3 || name.Length > 50)
                    errors.Add($"Discrete Input row {i + 1}: Input Name must be 3-50 characters (current {name.Length}).");
                // CutOffTimeMs numeric-only via NumericInputBehavior already validated as uint
            }
            for (int i = 0; i < HoldingRegisterMaps.Count; i++)
            {
                var row = HoldingRegisterMaps[i];
                if (string.IsNullOrWhiteSpace(row.WpfDefine))
                    errors.Add($"Holding row {i + 1}: Function defenation / WPF Define is required.");
            }

            return errors.Distinct().ToArray();
        }

        // ---- Connection lifecycle ----------------------------------------------------------

        private async Task ToggleConnectionAsync()
        {
            if (IsConnected)
            {
                await DisconnectByUserAsync();
                return;
            }

            var errors = ValidateAll();
            if (errors.Length > 0)
            {
                LogError("[Validation Error] Cannot connect - fix configuration errors first: " + string.Join(" ", errors));
                return;
            }

            IsBusy = true;
            try
            {
                await DisconnectCoreAsync();

                _modbusClient = new ModbusRtuClient(BuildConfig());
                await _modbusClient.OpenAsync();

                IsConnected = true;
                LogInfo($"Modbus link opened on {SelectedPortName} ({SelectedBaudRate} baud, slave {SlaveAddressText}). Diagnostics enabled. Press 'Listen Inputs 0-7' to start live polling.");
            }
            catch (UnauthorizedAccessException)
            {
                await DisconnectCoreAsync();
                LogError($"[Hardware Error] Port {_selectedPortName} is locked by another application. Close it and retry.");
            }
            catch (IOException ex)
            {
                await DisconnectCoreAsync();
                LogError($"[Hardware Error] Could not open {_selectedPortName}: {ex.Message}");
            }
            catch (Exception ex)
            {
                await DisconnectCoreAsync();
                LogError($"[Unexpected Error] Connection failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DisconnectByUserAsync()
        {
            IsBusy = true;
            try
            {
                await DisconnectCoreAsync();
                LogInfo("Disconnected by user.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DisconnectCoreAsync()
        {
            await StopInputPollingAsync();
            IsListening = false;

            if (_modbusClient != null)
            {
                try { await _modbusClient.CloseAsync(); } catch { }
                _modbusClient.Dispose();
                _modbusClient = null;
            }
            IsConnected = false;
        }

        // ---- Auto-listen polling for discrete inputs 0-5 -----------------------------------

        private async Task StartInputPollingAsync()
        {
            await StopInputPollingAsync();
            _inputPollFirst = true;
            Array.Clear(_lastInputStates, 0, _lastInputStates.Length);
            _inputPollCts = new CancellationTokenSource();
            var token = _inputPollCts.Token;
            _inputPollTask = Task.Run(() => PollInputsLoopAsync(token), token);
        }

        private void StartInputPolling() => _ = StartInputPollingAsync();

        private async Task StopInputPollingAsync()
        {
            var cts = _inputPollCts;
            var task = _inputPollTask;
            _inputPollCts = null;
            _inputPollTask = null;

            try { cts?.Cancel(); } catch { }

            if (task != null)
            {
                try { await Task.WhenAny(task, Task.Delay(800)); } catch { }
            }

            try { cts?.Dispose(); } catch { }
        }

        // Backward-compatible sync wrapper (kept for any legacy call sites)
        private void StopInputPolling() => _ = StopInputPollingAsync();

        private async Task ToggleListeningAsync()
        {
            if (IsListening)
            {
                await StopInputPollingAsync();
                IsListening = false;
                LogInfo("Stopped listening inputs 0-7.");
                return;
            }

            if (!EnsureLinkOnline()) return;

            IsListening = true;
            LogInfo("Started listening inputs 0-7 - live state appears in LAST RESULT. Press Stop to end.");
            await StartInputPollingAsync();
        }

        private async Task PollInputsLoopAsync(CancellationToken ct)
        {
            // Small settle delay before first poll (RS-485 transceiver)
            try { await Task.Delay(150, ct); } catch (OperationCanceledException) { return; }

            while (!ct.IsCancellationRequested && IsConnected)
            {
                ModbusRtuClient? client = _modbusClient;
                if (client == null || !client.IsOpen)
                    break;

                try
                {
                    bool[] values = await client.ReadDiscreteInputsAsync(0, 8, ct);

                    bool hasChange = _inputPollFirst;
                    if (!_inputPollFirst)
                    {
                        for (int i = 0; i < 8; i++)
                            if (values[i] != _lastInputStates[i]) { hasChange = true; break; }
                    }

                    if (hasChange)
                    {
                        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
                        var active = Enumerable.Range(0, 8).Where(i => values[i]).ToArray();
                        string detail = string.Join(" ", Enumerable.Range(0, 8).Select(i => $"[{i}]={(values[i] ? "ON" : "OFF")}"));
                        string result;
                        if (active.Length == 0)
                            result = $"[{ts}] Inputs 0-7 = all OFF | {detail}";
                        else
                            result = $"[{ts}] INPUT Pin {string.Join(",", active)} = ON | {detail}";

                        // Marshal to UI thread for bound properties
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher != null && !dispatcher.CheckAccess())
                        {
                            dispatcher.Invoke(() =>
                            {
                                DiagnosticDisplayResult = result;
                                // Log only active edges to keep the console useful without spamming OFF polls
                                if (active.Length > 0) AppendStatusLog(result);
                            });
                        }
                        else
                        {
                            DiagnosticDisplayResult = result;
                            if (active.Length > 0) AppendStatusLog(result);
                        }

                        Array.Copy(values, _lastInputStates, 8);
                        _inputPollFirst = false;
                    }

                    await Task.Delay(200, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Graceful shutdown after Dispose - do not spam the log
                    break;
                }
                catch (InvalidOperationException)
                {
                    // Port/semaphore was disposed after user disconnect - exit silently
                    break;
                }
                catch (TimeoutException)
                {
                    // Transient RS-485 timeout - retry after a short pause without marking link lost
                    try { await Task.Delay(350, ct); } catch (OperationCanceledException) { break; }
                }
                catch (ModbusException ex)
                {
                    // Address / function not supported - log once and back off
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    string msg = $"[Protocol Error] Auto-listen 0-7 rejected: {ex.Message}";
                    if (dispatcher != null && !dispatcher.CheckAccess())
                        dispatcher.Invoke(() => AppendStatusLog(msg));
                    else
                        AppendStatusLog(msg);
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                }
                catch (IOException ex) when (ex.Message.Contains("CRC", StringComparison.OrdinalIgnoreCase))
                {
                    // Transient CRC noise on RS-485 - log as info and retry, do not drop the link
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    string msg = $"[Transient] CRC error on poll - retrying: {ex.Message}";
                    if (dispatcher != null && !dispatcher.CheckAccess())
                        dispatcher.Invoke(() => AppendStatusLog(msg));
                    else
                        AppendStatusLog(msg);
                    try { await Task.Delay(400, ct); } catch (OperationCanceledException) { break; }
                }
                catch (IOException ex)
                {
                    var msg = $"[Hardware Error] Auto-listen link lost: {ex.Message}";
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.CheckAccess())
                        dispatcher.Invoke(() => MarkLinkLost(msg));
                    else
                        MarkLinkLost(msg);
                    break;
                }
                catch (Exception ex) when (ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase))
                {
                    // Suppress disposed-object spam after disconnect
                    break;
                }
                catch (Exception ex)
                {
                    var msg = $"[Error] Auto-listen failed: {ex.Message}";
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.CheckAccess())
                        dispatcher.Invoke(() => AppendStatusLog(msg));
                    else
                        AppendStatusLog(msg);
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                }
            }

            // If loop exited due to error/link lost while user thought it was listening, reset button state
            if (IsListening && !ct.IsCancellationRequested)
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                    dispatcher.Invoke(() => IsListening = false);
                else
                    IsListening = false;
            }
        }

        // ---- One-shot port probe (works without a persistent connection) --------------------

        private async Task TestConnectionAsync()
        {
            var errors = ValidateAll();
            if (errors.Length > 0)
            {
                LogError("[Validation Error] Cannot test - fix configuration errors first: " + string.Join(" ", errors));
                return;
            }

            IsBusy = true;
            SetStatus($"Opening {SelectedPortName} at {SelectedBaudRate} baud...", isError: false);
            try
            {
                var config = BuildConfig();
                var (success, message) = await Task.Run(() => ProbeSerialLink(config));
                SetStatus(message, isError: !success);
            }
            catch (Exception ex)
            {
                SetStatus($"Test failed unexpectedly: {ex.Message}", isError: true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static (bool Success, string Message) ProbeSerialLink(PlcConfig config)
        {
            SerialPort? port = null;
            try
            {
                port = new SerialPort(config.PortName, config.BaudRate, config.Parity, config.DataBits, config.StopBits)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };
                port.Open();
                Thread.Sleep(150); // let the RS-485 transceiver settle

                return (true, $"Connection OK - {config.PortName} opened at {config.BaudRate} baud, " +
                              $"{config.DataBits}{config.Parity.ToString()[0]}{config.StopBits.ToString()[0]}, slave {config.SlaveAddress}.");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, $"Port {config.PortName} is in use by another application.");
            }
            catch (IOException ex)
            {
                return (false, $"Could not open {config.PortName}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to open {config.PortName}: {ex.Message}");
            }
            finally
            {
                try { if (port != null && port.IsOpen) port.Close(); } catch { }
                port?.Dispose();
            }
        }

        private async Task SaveSettingsAsync()
        {
            var errors = ValidateAll();
            if (errors.Length > 0)
            {
                LogError("[Validation Error] Cannot save - fix configuration errors first: " + string.Join(" ", errors));
                return;
            }

            IsBusy = true;
            try
            {
                await _configManager.SavePlcConfigAsync(BuildConfig());
                LogInfo($"PLC settings saved successfully at {DateTime.Now:HH:mm:ss}.");

                _ = _auditService.LogSettingsChangedAsync(
                    GetCurrentUsername(),
                    "PLC (Modbus) Settings",
                    null,
                    $"Port={_selectedPortName}, Baud={_selectedBaudRate}, Slave={SlaveAddressText}, HoldingMaps={HoldingRegisterMaps.Count}, DiscreteInputs={DiscreteInputMaps.Count}");
            }
            catch (Exception ex)
            {
                LogError($"[Error] Save failed: {ex.Message}");
                _ = _auditService.LogErrorAsync(GetCurrentUsername(), "Save PLC settings", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // =====================================================================================
        //  MANUAL WRITE
        // =====================================================================================

        private async Task ExecuteManualWriteCommandAsync()
        {
            // All PLC outputs are holding registers - every manual write uses FC06.
            // FC02 (discrete inputs) and FC03/FC06 (holding registers) are independent
            // address spaces, so address 0 as FC02 (input) is distinct from address 0 as
            // FC06 (holding register D0). ModbusPoll proves FC06 @ 0 works (01 06 00 00 00 01 48 0A => OK),
            // so we no longer block 0-7 and we allow any 0-65535 holding-register address.

            if (!EnsureLinkOnline()) return;

            ushort address = ManualWriteAddress;

            IsBusy = true;
            try
            {
                var client = RequireClient();

                // Refactored: output window is now 0-5 (D0-D5, 6 registers). Poll proves 01 06 00 00 00 01 48 0A => OK at 0.
                // Any 0-65535 still allowed; 0-5 is the default hint.
                if (!PlcConfig.IsOutputRegister(address))
                {
                    LogInfo($"Hint: address {address} is outside the default output window 0-5 - will still attempt FC06 (any holding register 0-65535 valid).");
                }

                // Parse payload as holding-register value. Accept binary tokens as 1/0 for convenience,
                // otherwise require 0-65535 decimal or 0x hex.
                ushort value;
                if (TryParseStrictBinary(ManualWriteValue, out bool bitValue))
                {
                    value = bitValue ? (ushort)1 : (ushort)0;
                }
                else if (!TryParseUshort(ManualWriteValue, out value))
                {
                    LogError($"[Validation Error] '{ManualWriteValue}' is not a valid register value (expected 0-65535, decimal or 0x hex, or 0/1/true/false).");
                    return;
                }

                await client.WriteSingleRegisterAsync(address, value);

                DiagnosticDisplayResult = $"WRITE Register @ {address} = {value} (0x{value:X4}) - OK (FC06)";
                LogInfo($"WRITE Register (FC06) @ {address} = {value} (0x{value:X4}) OK.");
                SetStatus($"Register {address} written = {value}.", isError: false);
            }
            catch (TimeoutException ex)
            {
                LogTransportTimeout("Write", ex);
            }
            catch (ModbusException ex)
            {
                LogProtocolRejection("Write", address, ex);
            }
            catch (UnauthorizedAccessException)
            {
                MarkLinkLost($"[Hardware Error] Port {_selectedPortName} was claimed by another process during the write.");
            }
            catch (IOException ex)
            {
                MarkLinkLost($"[Hardware Error] Serial failure during write: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                MarkLinkLost("[Hardware Error] The serial stream was closed unexpectedly during the write.");
            }
            catch (Exception ex)
            {
                LogError($"[Unexpected Error] Write @ {address} failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // =====================================================================================
        //  MANUAL READ
        // =====================================================================================

        private async Task ExecuteManualReadCommandAsync()
        {
            if (!EnsureLinkOnline()) return;

            var config = BuildConfig();
            ushort address = ManualReadAddress;

            IsBusy = true;
            try
            {
                var client = RequireClient();

                // Verified PLC map: inputs 0-7 via FC02, outputs holding registers 0-5 via FC03 (FC06 for writes).
                if (PlcConfig.IsInputAddress(address))
                {
                    try
                    {
                        bool value = await client.ReadDiscreteInputAsync(address);
                        PublishReadResult(address == config.ItemArrivedSensorAddress ? "Input Pin (sensor)" : "Input Pin", address, value);
                        return;
                    }
                    catch (ModbusException)
                    {
                        LogInfo($"Address {address} did not respond as a discrete input - probing holding register...");
                    }
                }

                if (PlcConfig.IsOutputRegister(address) ||
                    address == config.UnderWeightBuzzerAddress ||
                    address == config.OverWeightBuzzerAddress ||
                    address == config.AcceptSignalAddress)
                {
                    ushort value = await client.ReadHoldingRegisterAsync(address);
                    PublishRegisterReadResult(address, value);
                    return;
                }

                // Generic address: try holding register first (new PLC logic), then discrete input, then legacy coil for diagnostics.
                try
                {
                    ushort value = await client.ReadHoldingRegisterAsync(address);
                    PublishRegisterReadResult(address, value);
                }
                catch (ModbusException)
                {
                    try
                    {
                        bool value = await client.ReadDiscreteInputAsync(address);
                        PublishReadResult("Discrete Input", address, value);
                    }
                    catch (ModbusException)
                    {
                        // Legacy fallback - coils are no longer used for outputs but keep for diagnostics.
#pragma warning disable CS0618
                        bool value = await client.ReadCoilAsync(address);
#pragma warning restore CS0618
                        PublishReadResult("Coil (legacy)", address, value);
                    }
                }
            }
            catch (TimeoutException ex)
            {
                LogTransportTimeout("Read", ex);
            }
            catch (ModbusException ex)
            {
                LogProtocolRejection("Read", address, ex);
            }
            catch (UnauthorizedAccessException)
            {
                MarkLinkLost($"[Hardware Error] Port {_selectedPortName} was claimed by another process during the read.");
            }
            catch (IOException ex)
            {
                MarkLinkLost($"[Hardware Error] Serial failure during read: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                MarkLinkLost("[Hardware Error] The serial stream was closed unexpectedly during the read.");
            }
            catch (Exception ex)
            {
                LogError($"[Unexpected Error] Read @ {address} failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // =====================================================================================
        //  LOGGING, VALIDATION AND TRANSPORT HELPERS
        // =====================================================================================

        /// <summary>Guards every diagnostic operation: requires an open link before any bytes are sent.</summary>
        private bool EnsureLinkOnline()
        {
            if (!IsConnected || _modbusClient == null || !_modbusClient.IsOpen)
            {
                LogError("[Error] Cannot send command. Connection is offline.");
                return false;
            }
            return true;
        }

        private ModbusRtuClient RequireClient()
            => _modbusClient ?? throw new InvalidOperationException("Modbus client is not initialised.");

        // ---- Write-target probes (side-effect-free reads used before a manual write) ----

        /// <summary>True when the PLC answers an FC01 coil read at the address (legacy - outputs now use FC03/FC06).</summary>
#pragma warning disable CS0618 // legacy coil probe retained for diagnostics
        private static async Task<bool> ProbeIsCoilAsync(ModbusRtuClient client, ushort address)
        {
            try { await client.ReadCoilAsync(address); return true; }
            catch (ModbusException) { return false; }
        }
#pragma warning restore CS0618

        /// <summary>True when the PLC answers an FC02 discrete-input read at the address.</summary>
        private static async Task<bool> ProbeIsDiscreteInputAsync(ModbusRtuClient client, ushort address)
        {
            try { await client.ReadDiscreteInputAsync(address); return true; }
            catch (ModbusException) { return false; }
        }

        /// <summary>True when the PLC answers an FC03 holding-register read at the address.</summary>
        private static async Task<bool> ProbeIsHoldingRegisterAsync(ModbusRtuClient client, ushort address)
        {
            try { await client.ReadHoldingRegisterAsync(address); return true; }
            catch (ModbusException) { return false; }
        }

        /// <summary>Strictly binary coil payload parser: only 1 / 0 / true / false pass.</summary>
        private static bool TryParseStrictBinary(string text, out bool value)
        {
            var t = text.Trim().ToLowerInvariant();
            if (BinaryTrueTokens.Contains(t)) { value = true; return true; }
            if (BinaryFalseTokens.Contains(t)) { value = false; return true; }
            value = false;
            return false;
        }

        /// <summary>Detects pure numeric input so the coil validation can give a targeted hint.</summary>
        private static bool IsNumericInput(string text)
            => !string.IsNullOrWhiteSpace(text) && text.Trim().All(char.IsDigit);

        /// <summary>Parses unsigned 16-bit values in decimal or 0x-prefixed hex.</summary>
        private static bool TryParseUshort(string text, out ushort value)
        {
            value = 0;
            var t = text.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ushort.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

            return ushort.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private void PublishReadResult(string kind, ushort address, bool value)
        {
            DiagnosticDisplayResult = $"READ {kind} @ {address} = {(value ? "TRUE" : "FALSE")} - OK";
            LogInfo($"READ {kind} @ {address} = {(value ? "TRUE" : "FALSE")} OK.");
            SetStatus($"{kind} {address} = {(value ? "TRUE" : "FALSE")}.", isError: false);
        }

        private void PublishRegisterReadResult(ushort address, ushort value)
        {
            DiagnosticDisplayResult = $"READ Holding Register @ {address} = {value} (0x{value:X4}) - OK";
            LogInfo($"READ Holding Register @ {address} = {value} (0x{value:X4}) OK.");
            SetStatus($"Register {address} = {value}.", isError: false);
        }

        /// <summary>Safe failure state for timeouts: keep the port but surface the physical link checklist.</summary>
        private void LogTransportTimeout(string operation, Exception ex)
        {
            var cfg = BuildConfig();
            LogError($"[Hardware Error] {operation} timed out. Check the RS-485 A/B line wiring and 120-ohm termination on the " +
                     $"Wecon LX3V, confirm the PLC is powered, and verify Slave ID {cfg.SlaveAddress}, {cfg.BaudRate} baud, " +
                     $"{cfg.DataBits}{cfg.Parity.ToString()[0]}{cfg.StopBits.ToString()[0]} match the PLC COM settings on {cfg.PortName}. ({ex.Message})");
        }

        /// <summary>Translates Modbus exception codes into targeted technician guidance.</summary>
        private void LogProtocolRejection(string operation, ushort address, ModbusException ex)
        {
            var detail = ex.Code switch
            {
                0x01 => $"function code not supported at address {address}.",
                0x02 => "the requested data address does not exist on this device.",
                0x03 => "a value in the request was out of range for the device.",
                0x04 => "the slave hit an internal fault while executing the request.",
                0x06 => "the slave is busy - retry shortly.",
                _ => $"slave returned exception code 0x{ex.Code:X2}."
            };

            if (ex.Code is 0x01 or 0x02)
                LogError($"[Protocol Error] The PLC rejected the request. Verify the target memory register address exists on the device. ({detail})");
            else
                LogError($"[Protocol Error] {operation} rejected by the slave: {detail}");
        }

        /// <summary>Reflects a dead adapter/link in the connection state and logs it.</summary>
        private void MarkLinkLost(string message)
        {
            LogError(message);
            IsConnected = false;
        }

        /// <summary>Appends a timestamped info line to the log and mirrors it in the status bar.</summary>
        private void LogInfo(string message)
        {
            AppendStatusLog(message);
            SetStatus(message, isError: false);
        }

        /// <summary>Appends a timestamped error line to the log and flags the status bar red.</summary>
        private void LogError(string message)
        {
            AppendStatusLog(message);
            SetStatus(message, isError: true);
        }

        private void AppendStatusLog(string message)
        {
            _statusLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");

            var text = _statusLog.ToString();
            if (text.Length > MaxStatusLogChars)
            {
                text = text[^MaxStatusLogChars..];
                _statusLog.Clear();
                _statusLog.Append(text);
            }

            StatusLogMessage = text;
        }

        private void SetStatus(string message, bool isError)
        {
            IsStatusError = isError;
            StatusMessage = message;
        }
    }

    public class HoldingRegisterMapRow : BaseViewModel
    {
        private ushort _address;
        public ushort Address { get => _address; set => SetProperty(ref _address, value); }

        private ushort _value;
        public ushort Value { get => _value; set => SetProperty(ref _value, value); }

        private string _wpfDefine = string.Empty;
        /// <summary>Normalized short key: idleReset|ok|under|over|buzzer|reject — display text resolved via PlcConfig.GetWpfDefineDisplay().</summary>
        public string WpfDefine { get => _wpfDefine; set => SetProperty(ref _wpfDefine, ProWeighSuitePlatforms.Models.PlcConfig.NormalizeWpfDefine(value)); }

        public string WpfDefineDisplay => ProWeighSuitePlatforms.Models.PlcConfig.GetWpfDefineDisplay(_wpfDefine);
    }

    public class DiscreteInputMapRow : BaseViewModel
    {
        private ushort _address;
        public ushort Address { get => _address; set => SetProperty(ref _address, value); }

        private ushort _value;
        public ushort Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    RaisePropertyChanged(nameof(IsValueTrue));
                    RaisePropertyChanged(nameof(IsValueFalse));
                }
            }
        }

        public bool IsValueTrue
        {
            get => _value == 1;
            set { if (value && _value != 1) Value = 1; }
        }

        public bool IsValueFalse
        {
            get => _value == 0;
            set { if (value && _value != 0) Value = 0; }
        }

        private string _wpfDefine = string.Empty;
        /// <summary>Normalized short key: weighingInProcess|capture — display via PlcConfig.GetDiscreteWpfDisplay().</summary>
        public string WpfDefine { get => _wpfDefine; set => SetProperty(ref _wpfDefine, ProWeighSuitePlatforms.Models.PlcConfig.NormalizeDiscreteWpfDefine(value)); }

        public string WpfDefineDisplay => ProWeighSuitePlatforms.Models.PlcConfig.GetDiscreteWpfDisplay(_wpfDefine);

        private string _inputName = string.Empty;
        /// <summary>Input friendly name (3-50 chars).</summary>
        public string InputName
        {
            get => _inputName;
            set
            {
                var v = value ?? string.Empty;
                if (v.Length > 50) v = v.Substring(0, 50);
                SetProperty(ref _inputName, v);
            }
        }

        private uint _CutOffTimeMs = 50;
        /// <summary>Debounce time in milliseconds — numeric only.</summary>
        public uint CutOffTimeMs { get => _CutOffTimeMs; set => SetProperty(ref _CutOffTimeMs, value); }

        // String wrapper for TextBox binding (allows empty while typing, numeric behavior handles validation)
        public string CutOffTimeMsText
        {
            get => _CutOffTimeMs.ToString();
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { SetProperty(ref _CutOffTimeMs, 0u, nameof(CutOffTimeMs)); RaisePropertyChanged(nameof(CutOffTimeMsText)); return; }
                if (uint.TryParse(value, out var v)) SetProperty(ref _CutOffTimeMs, v, nameof(CutOffTimeMs));
                RaisePropertyChanged(nameof(CutOffTimeMsText));
            }
        }
    }
}

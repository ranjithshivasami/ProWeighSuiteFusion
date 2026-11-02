using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Helpers;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class PrinterConnectionViewModel : BaseViewModel
    {
        public string Title => "Printer Configuration";

        private readonly PrinterConfigurationService _configService = new();
        private readonly AuditService _auditService = new();
        private readonly string _currentUsername;
        private bool _isBusy;

        // --- Connection Type ---

        public List<string> ConnectionTypes { get; } = new() { "USB", "COM", "Ethernet" };

        private string _selectedConnectionType = "USB";
        public string SelectedConnectionType
        {
            get => _selectedConnectionType;
            set
            {
                if (SetProperty(ref _selectedConnectionType, value))
                {
                    RaisePropertyChanged(nameof(IsSerialMode));
                    RaisePropertyChanged(nameof(IsComMode));
                    RaisePropertyChanged(nameof(IsEthernetMode));
                }
            }
        }

        public bool IsSerialMode => SelectedConnectionType is "USB" or "COM";
        public bool IsComMode => SelectedConnectionType == "COM";
        public bool IsEthernetMode => SelectedConnectionType == "Ethernet";

        // --- Serial Settings ---

        public ObservableCollection<string> AvailablePorts { get; } = new();
        public List<int> BaudRates => SerialPortHelper.GetBaudRates();
        public List<int> DataBitsOptions => SerialPortHelper.GetDataBitsOptions();
        public List<string> ParityOptions => SerialPortHelper.GetParityOptions();
        public List<string> StopBitsOptions => SerialPortHelper.GetStopBitsOptions();
        public List<string> FlowControlOptions => SerialPortHelper.GetFlowControlOptions();

        private string _selectedComPort = string.Empty;
        public string SelectedComPort
        {
            get => _selectedComPort;
            set => SetProperty(ref _selectedComPort, value);
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

        private string _selectedParity = "None";
        public string SelectedParity
        {
            get => _selectedParity;
            set => SetProperty(ref _selectedParity, value);
        }

        private string _selectedStopBits = "1";
        public string SelectedStopBits
        {
            get => _selectedStopBits;
            set => SetProperty(ref _selectedStopBits, value);
        }

        private string _selectedFlowControl = "None";
        public string SelectedFlowControl
        {
            get => _selectedFlowControl;
            set => SetProperty(ref _selectedFlowControl, value);
        }

        // --- Ethernet Settings ---

        private string _ipAddress = string.Empty;
        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                if (SetProperty(ref _ipAddress, value))
                    RaisePropertyChanged(nameof(IpValidationMessage));
            }
        }

        private int _port = 9100;
        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        private string _subnetMask = string.Empty;
        public string SubnetMask
        {
            get => _subnetMask;
            set => SetProperty(ref _subnetMask, value);
        }

        private string _gateway = string.Empty;
        public string Gateway
        {
            get => _gateway;
            set => SetProperty(ref _gateway, value);
        }

        public string IpValidationMessage =>
            string.IsNullOrWhiteSpace(IpAddress) ? string.Empty :
            NetworkPrinterHelper.IsValidIpAddress(IpAddress) ? string.Empty : "Invalid IP address format";

        // --- Status ---

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        // --- Discovered Printers ---

        public ObservableCollection<string> DiscoveredPrinters { get; } = new();

        private string? _selectedDiscoveredPrinter;
        public string? SelectedDiscoveredPrinter
        {
            get => _selectedDiscoveredPrinter;
            set
            {
                if (SetProperty(ref _selectedDiscoveredPrinter, value) && value != null)
                    IpAddress = value;
            }
        }

        // --- Commands ---

        public ICommand RefreshPortsCommand { get; }
        public ICommand DetectPrinterCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand FindPrintersCommand { get; }
        public ICommand TestPrintCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand LoadDefaultsCommand { get; }

        public PrinterConnectionViewModel(string currentUsername = "System")
        {
            _currentUsername = currentUsername;
            RefreshPortsCommand = new RelayCommand(_ => RefreshPorts(), _ => !IsBusy);
            DetectPrinterCommand = new RelayCommand(async _ => await DetectPrinterAsync(), _ => !IsBusy && IsSerialMode && !string.IsNullOrEmpty(SelectedComPort));
            TestConnectionCommand = new RelayCommand(async _ => await TestConnectionAsync(), _ => !IsBusy && IsEthernetMode && !string.IsNullOrEmpty(IpAddress));
            FindPrintersCommand = new RelayCommand(async _ => await FindPrintersAsync(), _ => !IsBusy && IsEthernetMode);
            TestPrintCommand = new RelayCommand(async _ => await TestPrintAsync(), _ => !IsBusy);
            SaveCommand = new RelayCommand(async _ => await SaveConfigAsync(), _ => !IsBusy);
            LoadDefaultsCommand = new RelayCommand(_ => ApplyDefaults(), _ => !IsBusy);

            // Load saved config on creation
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                RefreshPorts();
                var config = await _configService.LoadAsync();
                ApplyConfig(config);
                StatusMessage = "Configuration loaded.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load configuration: {ex.Message}";
            }
        }

        private void RefreshPorts()
        {
            AvailablePorts.Clear();
            foreach (var port in SerialPortHelper.GetAvailablePorts())
                AvailablePorts.Add(port);

            if (AvailablePorts.Count > 0 && !AvailablePorts.Contains(SelectedComPort))
                SelectedComPort = AvailablePorts[0];

            StatusMessage = AvailablePorts.Count > 0
                ? $"{AvailablePorts.Count} COM port(s) found."
                : "No COM ports detected.";
        }

        private async Task DetectPrinterAsync()
        {
            if (string.IsNullOrEmpty(SelectedComPort)) return;

            IsBusy = true;
            StatusMessage = $"Detecting printer on {SelectedComPort}...";
            IsConnected = false;

            try
            {
                var (success, message) = await SerialPortHelper.DetectPrinterAsync(
                    SelectedComPort, SelectedBaudRate, SelectedDataBits,
                    SelectedParity, SelectedStopBits, SelectedFlowControl);

                IsConnected = success;
                StatusMessage = message;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Detection error: {ex.Message}";
                IsConnected = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task TestConnectionAsync()
        {
            IsBusy = true;
            StatusMessage = $"Testing connection to {IpAddress}:{Port}...";
            IsConnected = false;

            try
            {
                // First ping
                var (pingOk, pingMsg) = await NetworkPrinterHelper.TestConnectionAsync(IpAddress);
                if (!pingOk)
                {
                    StatusMessage = pingMsg;
                    return;
                }

                // Then test TCP port
                var (tcpOk, tcpMsg) = await NetworkPrinterHelper.TestTcpConnectionAsync(IpAddress, Port);
                IsConnected = tcpOk;
                StatusMessage = tcpOk
                    ? $"Connected! {pingMsg} | {tcpMsg}"
                    : $"Ping OK but port unreachable. {tcpMsg}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Connection test error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task FindPrintersAsync()
        {
            IsBusy = true;
            DiscoveredPrinters.Clear();
            var searchIp = !string.IsNullOrWhiteSpace(IpAddress) ? IpAddress : "192.168.1.1";
            StatusMessage = $"Scanning subnet for printers (port {Port})... This may take a moment.";

            try
            {
                var printers = await NetworkPrinterHelper.FindPrintersOnSubnetAsync(searchIp, Port);

                foreach (var ip in printers)
                    DiscoveredPrinters.Add(ip);

                StatusMessage = printers.Count > 0
                    ? $"Found {printers.Count} device(s) listening on port {Port}."
                    : "No printers found on the subnet.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Network scan error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task TestPrintAsync()
        {
            IsBusy = true;
            StatusMessage = "Sending test label...";
            bool success = false;
            string connectionInfo = IsSerialMode ? $"{SelectedConnectionType} ({SelectedComPort})" : $"Ethernet ({IpAddress}:{Port})";

            try
            {
                var zpl = ZplTestHelper.GetTestZpl();
                string message;

                if (IsSerialMode)
                {
                    if (string.IsNullOrEmpty(SelectedComPort))
                    {
                        StatusMessage = "Please select a COM port.";
                        return;
                    }

                    (success, message) = await SerialPortHelper.SendZplViaSerialAsync(
                        SelectedComPort, SelectedBaudRate, SelectedDataBits,
                        SelectedParity, SelectedStopBits, SelectedFlowControl, zpl);
                }
                else
                {
                    if (!NetworkPrinterHelper.IsValidIpAddress(IpAddress))
                    {
                        StatusMessage = "Please enter a valid IP address.";
                        return;
                    }

                    (success, message) = await NetworkPrinterHelper.SendZplViaTcpAsync(IpAddress, Port, zpl);
                }

                IsConnected = success;
                StatusMessage = success ? $"Test print sent! {message}" : $"Test print failed. {message}";
                _ = _auditService.LogPrinterTestPrintAsync(_currentUsername, connectionInfo, success);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Test print error: {ex.Message}";
                IsConnected = false;
                _ = _auditService.LogPrinterTestPrintAsync(_currentUsername, connectionInfo, false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveConfigAsync()
        {
            if (!Validate(out var validationMsg))
            {
                StatusMessage = validationMsg;
                return;
            }

            IsBusy = true;
            StatusMessage = "Saving configuration...";

            try
            {
                var config = BuildConfig();
                await _configService.SaveAsync(config);
                StatusMessage = "Configuration saved successfully.";
                _ = _auditService.LogPrinterConfigSavedAsync(_currentUsername, config);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to save configuration: {ex.Message}";
                _ = _auditService.LogErrorAsync(_currentUsername, "Save printer configuration", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyDefaults()
        {
            var defaults = PrinterConfigurationService.GetDefaultConfig();
            ApplyConfig(defaults);
            StatusMessage = "Default ZT231 settings applied.";
            _ = _auditService.LogAsync(new AuditEntry
            {
                Action = "PrinterConfig_DefaultsApplied",
                Category = AuditCategory.Configuration,
                Username = _currentUsername,
                EntityType = "PrinterConfig",
                Details = "Default ZT231 printer settings applied",
                Severity = AuditSeverity.Info
            });
        }

        private void ApplyConfig(ZT231PrinterConfig config)
        {
            SelectedConnectionType = config.ConnectionType switch
            {
                "USB/COM" => "USB",
                _ => config.ConnectionType
            };
            SelectedComPort = config.ComPort;
            SelectedBaudRate = config.BaudRate;
            SelectedDataBits = config.DataBits;
            SelectedParity = config.Parity;
            SelectedStopBits = config.StopBits;
            SelectedFlowControl = config.FlowControl;
            IpAddress = config.IpAddress;
            Port = config.Port;
            SubnetMask = config.SubnetMask;
            Gateway = config.Gateway;
        }

        private ZT231PrinterConfig BuildConfig()
        {
            return new ZT231PrinterConfig
            {
                ConnectionType = SelectedConnectionType,
                ComPort = SelectedComPort,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                Parity = SelectedParity,
                StopBits = SelectedStopBits,
                FlowControl = SelectedFlowControl,
                IpAddress = IpAddress,
                Port = Port,
                SubnetMask = SubnetMask,
                Gateway = Gateway
            };
        }

        private bool Validate(out string message)
        {
            if (IsSerialMode)
            {
                if (string.IsNullOrWhiteSpace(SelectedComPort))
                {
                    message = "Please select a COM port.";
                    return false;
                }
            }
            else
            {
                if (!NetworkPrinterHelper.IsValidIpAddress(IpAddress))
                {
                    message = "Please enter a valid IP address.";
                    return false;
                }
                if (!NetworkPrinterHelper.IsValidPort(Port))
                {
                    message = "Please enter a valid port number (1-65535).";
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }
    }
}

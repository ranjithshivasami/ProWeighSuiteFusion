using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Services.Scanners;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class ScannerSettingsViewModel : BaseViewModel
    {
        public string Title => "Scanner Settings";

        private readonly AppSettingsService _svc = new();
        private readonly AuditService _audit = new();
        private readonly string _user;
        private AppSettings _settings = new();

        public IReadOnlyList<ScannerBrandDescriptor> Brands => ScannerCatalog.Brands;

        private string _brand = "None";
        public string Brand
        {
            get => _brand;
            set
            {
                var n = ScannerSettings.NormalizeBrand(value);
                if (_brand == n) return;
                _brand = n;
                _settings.Scanner.Brand = n;
                RaisePropertyChanged(nameof(Brand));
                RaisePropertyChanged(nameof(IsScannerEnabled));
                RaisePropertyChanged(nameof(IsSrk));
                RaisePropertyChanged(nameof(IsLan));
                RaisePropertyChanged(nameof(IsSerial));
                RaisePropertyChanged(nameof(CanTest));
            }
        }

        public bool IsScannerEnabled => !string.Equals(_brand, "None", StringComparison.OrdinalIgnoreCase);

        public bool IsSrk => string.Equals(_brand, "Srk", StringComparison.OrdinalIgnoreCase);

        private string _connectionType = "LAN";
        public string ConnectionType
        {
            get => _connectionType;
            set { _connectionType = ScannerSettings.NormalizeConnection(value); _settings.Scanner.ConnectionType = _connectionType; RaisePropertyChanged(nameof(ConnectionType)); RaisePropertyChanged(nameof(IsLan)); RaisePropertyChanged(nameof(IsSerial)); }
        }
        public bool IsLan => IsSrk && string.Equals(_connectionType, "LAN", StringComparison.OrdinalIgnoreCase);
        public bool IsSerial => IsSrk && !string.Equals(_connectionType, "LAN", StringComparison.OrdinalIgnoreCase);

        private string _ipAddress = "192.168.1.250";
        public string IpAddress { get => _ipAddress; set { _ipAddress = (value ?? "").Trim(); _settings.Scanner.IpAddress = _ipAddress; RaisePropertyChanged(nameof(IpAddress)); } }

        private int _tcpPort = 60000;
        public int TcpPort { get => _tcpPort; set { _tcpPort = value <= 0 ? 60000 : value; _settings.Scanner.TcpPort = _tcpPort; RaisePropertyChanged(nameof(TcpPort)); } }

        private string _comPort = "COM1";
        public string ComPort { get => _comPort; set { _comPort = (value ?? "COM1").Trim(); _settings.Scanner.ComPort = _comPort; RaisePropertyChanged(nameof(ComPort)); } }

        private int _baudRate = 115200;
        public int BaudRate { get => _baudRate; set { _baudRate = value <= 0 ? 115200 : value; _settings.Scanner.BaudRate = _baudRate; RaisePropertyChanged(nameof(BaudRate)); } }

        private int _powerDbm = 30;
        public int PowerDbm { get => _powerDbm; set { _powerDbm = Math.Clamp(value, 0, 30); _settings.Scanner.PowerDbm = _powerDbm; RaisePropertyChanged(nameof(PowerDbm)); } }

        private int _dedupMs = 2000;
        public int DedupMs { get => _dedupMs; set { _dedupMs = Math.Max(0, value); _settings.Scanner.DedupMs = _dedupMs; RaisePropertyChanged(nameof(DedupMs)); } }

        private string _epcFilterPrefix = string.Empty;
        public string EpcFilterPrefix { get => _epcFilterPrefix; set { _epcFilterPrefix = (value ?? "").Trim().ToUpperInvariant(); _settings.Scanner.EpcFilterPrefix = _epcFilterPrefix; RaisePropertyChanged(nameof(EpcFilterPrefix)); } }

        private string _inventoryMode = "Continuous";
        public string InventoryMode
        {
            get => _inventoryMode;
            set
            {
                var n = string.Equals((value ?? "").Trim(), "Triggered", StringComparison.OrdinalIgnoreCase) ? "Triggered" : "Continuous";
                _inventoryMode = n; _settings.Scanner.InventoryMode = n; RaisePropertyChanged(nameof(InventoryMode)); RaisePropertyChanged(nameof(InventoryWarning));
            }
        }
        public List<string> InventoryModes { get; } = new() { "Continuous", "Triggered" };

        private string _filter = string.Empty;
        public string Filter
        {
            get => _filter;
            set { _filter = value ?? string.Empty; _settings.Scanner.ScannerIdFilter = _filter.Trim(); RaisePropertyChanged(nameof(Filter)); }
        }

        private bool _autoTrigger = true;
        public bool AutoTrigger
        {
            get => _autoTrigger;
            set { _autoTrigger = value; _settings.Scanner.AutoTriggerOnScan = value; RaisePropertyChanged(nameof(AutoTrigger)); }
        }

        public ObservableCollection<DiscoveredScanner> Discovered { get; } = new();

        // ---- Tag inventory (test-only panel, styled like the vendor reader tool) ----
        public class TagInventoryRow : BaseViewModel
        {
            public int Index { get; set; }
            public string Label { get; set; } = string.Empty;
            public string RawHex { get; set; } = string.Empty;

            private string _time = string.Empty;
            public string Time { get => _time; set => SetProperty(ref _time, value); }

            private string _tagId = string.Empty;
            public string TagId { get => _tagId; set => SetProperty(ref _tagId, value); }

            private string _length = string.Empty;
            public string Length { get => _length; set => SetProperty(ref _length, value); }

            private string _antenna = string.Empty;
            public string Antenna { get => _antenna; set => SetProperty(ref _antenna, value); }

            private int _count;
            public int Count { get => _count; set => SetProperty(ref _count, value); }

            private string _rssi = "-";
            public string Rssi { get => _rssi; set => SetProperty(ref _rssi, value); }
        }

        public ObservableCollection<TagInventoryRow> TagRows { get; } = new();
        private readonly Dictionary<string, TagInventoryRow> _tagIndex = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _inventoryCts;

        public int TagCnt => TagRows.Count;

        private int _totalTagReads;
        public int TotalTagReads { get => _totalTagReads; private set => SetProperty(ref _totalTagReads, value); }

        public List<string> ShowModes { get; } = new() { "HEX", "ASCII" };

        private string _showMode = "HEX";
        public string ShowMode
        {
            get => _showMode;
            set
            {
                var n = string.Equals((value ?? "").Trim(), "ASCII", StringComparison.OrdinalIgnoreCase) ? "ASCII" : "HEX";
                if (_showMode == n) return;
                _showMode = n;
                RaisePropertyChanged(nameof(ShowMode));
                foreach (var row in TagRows) ApplyShowMode(row);
            }
        }

        public string InventoryWarning
        {
            get
            {
                if (IsInventoryRunning) return "Inventory running – present tags in front of the antenna, then press Stop.";
                if (IsSrk && string.Equals(_inventoryMode, "Triggered", StringComparison.OrdinalIgnoreCase))
                    return "Warning: please set Mode to Continuous for ActiveMode inventory test.";
                return string.Empty;
            }
        }

        private bool _isInventoryRunning;
        public bool IsInventoryRunning
        {
            get => _isInventoryRunning;
            private set { SetProperty(ref _isInventoryRunning, value); RaisePropertyChanged(nameof(InventoryWarning)); CommandManager.InvalidateRequerySuggested(); }
        }

        private static void ApplyShowModeStatic(TagInventoryRow row, string showMode)
        {
            var hex = (row.RawHex ?? string.Empty).Trim();
            var label = (row.Label ?? string.Empty).Trim();
            row.TagId = string.Equals(showMode, "ASCII", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrEmpty(label) ? hex : label)
                : (string.IsNullOrEmpty(hex) ? label : hex);
        }

        private void ApplyShowMode(TagInventoryRow row) => ApplyShowModeStatic(row, _showMode);

        private static string EpcByteLength(string? hex)
        {
            var clean = new string((hex ?? string.Empty).Where(Uri.IsHexDigit).ToArray());
            return (clean.Length / 2).ToString("D2");
        }

        private void OnTagRead(ScannerBarcodeEventArgs e)
        {
            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.Invoke(() =>
            {
                var raw = (e.RawHex ?? string.Empty).Trim();
                var label = (e.Label ?? string.Empty).Trim();
                var key = !string.IsNullOrEmpty(raw) ? raw : label;
                if (string.IsNullOrEmpty(key)) return;
                TotalTagReads++;
                if (_tagIndex.TryGetValue(key, out var row))
                {
                    row.Count++;
                    row.Time = DateTime.Now.ToString("HH:mm:ss");
                }
                else
                {
                    if (TagRows.Count >= 500) return;
                    row = new TagInventoryRow
                    {
                        Index = TagRows.Count + 1,
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Label = label,
                        RawHex = raw,
                        Length = EpcByteLength(!string.IsNullOrEmpty(raw) ? raw : label),
                        Antenna = e.ScannerId > 0 ? e.ScannerId.ToString("D2") : "01",
                        Count = 1,
                        Rssi = "-"
                    };
                    ApplyShowMode(row);
                    TagRows.Add(row);
                    _tagIndex[key] = row;
                    RaisePropertyChanged(nameof(TagCnt));
                }
            });
        }

        private async Task StartInventoryAsync()
        {
            if (IsInventoryRunning) return;
            IsInventoryRunning = true;
            _inventoryCts = new CancellationTokenSource();
            IScannerProvider? provider = null;
            try
            {
                Status = "Saving current settings, then starting inventory...";
                await PersistLiveSettingsAsync();
                var live = BuildLiveSettings();
                var factory = new ScannerProviderFactory();
                provider = factory.Create(_brand);
                ApplyOverride(provider, live);
                provider.BarcodeScanned += OnTagRead2;
                Status = $"Inventory running on {(string.Equals(live.ConnectionType, "LAN", StringComparison.OrdinalIgnoreCase) ? $"{live.IpAddress}:{live.TcpPort}" : $"{live.ComPort}@{live.BaudRate}")} ({live.InventoryMode}). Present tags, then press Stop.";
                await provider.StartAsync();
                try { await Task.Delay(Timeout.Infinite, _inventoryCts.Token); }
                catch (TaskCanceledException) { }
                Status = $"Inventory stopped. {TagRows.Count} tag(s), {TotalTagReads} read(s).";
            }
            catch (Exception ex) { Status = $"Inventory failed: {ex.Message}"; }
            finally
            {
                try
                {
                    if (provider != null)
                    {
                        provider.BarcodeScanned -= OnTagRead2;
                        await provider.StopAsync();
                        await provider.DisposeAsync();
                    }
                }
                catch { }
                _inventoryCts?.Dispose();
                _inventoryCts = null;
                IsInventoryRunning = false;
            }
        }

        private void OnTagRead2(object? s, ScannerBarcodeEventArgs e) => OnTagRead(e);

        private void StopInventory() => _inventoryCts?.Cancel();

        private void ClearTags()
        {
            TagRows.Clear();
            _tagIndex.Clear();
            TotalTagReads = 0;
            RaisePropertyChanged(nameof(TagCnt));
        }

        private void ExportTags()
        {
            if (TagRows.Count == 0) return;
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"TagInventory_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    Filter = "CSV (*.csv)|*.csv"
                };
                if (dlg.ShowDialog() != true) return;
                var sb = new StringBuilder();
                sb.AppendLine("Index,Time,TagID,Length,Antenna,Count,RSSI");
                foreach (var r in TagRows)
                    sb.AppendLine($"{r.Index},{r.Time},{EscapeCsv(r.TagId)},{r.Length},{r.Antenna},{r.Count},{r.Rssi}");
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                Status = $"Exported {TagRows.Count} tag(s) to {dlg.FileName}.";
            }
            catch (Exception ex) { Status = $"Export failed: {ex.Message}"; }
        }

        private static string EscapeCsv(string? value)
        {
            value ??= string.Empty;
            return value.Contains(',') || value.Contains('"') || value.Contains('\n')
                ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private string _lastLabel = string.Empty;
        public string LastLabel { get => _lastLabel; set => SetProperty(ref _lastLabel, value); }

        private string _status = "Not connected";
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { SetProperty(ref _isBusy, value); CommandManager.InvalidateRequerySuggested(); } }

        public bool CanTest => IsScannerEnabled && !IsBusy;

        public ICommand RefreshCommand { get; }
        public ICommand TestScanCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand TriggerCommand { get; }
        public ICommand GetTagsCommand { get; }
        public ICommand StopTagsCommand { get; }
        public ICommand ClearTagsCommand { get; }
        public ICommand ExportTagsCommand { get; }

        public ScannerSettingsViewModel(string user = "System")
        {
            _user = user;
            RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsBusy);
            TestScanCommand = new RelayCommand(async _ => await TestAsync(), _ => CanTest);
            TriggerCommand = new RelayCommand(async _ => await TriggerAsync(), _ => CanTest);
            SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => !IsBusy);
            GetTagsCommand = new RelayCommand(async _ => await StartInventoryAsync(), _ => !IsBusy && !IsInventoryRunning && IsScannerEnabled);
            StopTagsCommand = new RelayCommand(_ => StopInventory(), _ => IsInventoryRunning);
            ClearTagsCommand = new RelayCommand(_ => ClearTags(), _ => !IsInventoryRunning && TagRows.Count > 0);
            ExportTagsCommand = new RelayCommand(_ => ExportTags(), _ => TagRows.Count > 0);
            TagRows.CollectionChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            _settings = await _svc.LoadAsync();
            _brand = ScannerSettings.NormalizeBrand(_settings.Scanner.Brand);
            _filter = _settings.Scanner.ScannerIdFilter ?? string.Empty;
            _autoTrigger = _settings.Scanner.AutoTriggerOnScan;
            _connectionType = ScannerSettings.NormalizeConnection(_settings.Scanner.ConnectionType);
            _ipAddress = string.IsNullOrWhiteSpace(_settings.Scanner.IpAddress) ? "192.168.1.250" : _settings.Scanner.IpAddress.Trim();
            _tcpPort = _settings.Scanner.TcpPort > 0 ? _settings.Scanner.TcpPort : 60000;
            _comPort = string.IsNullOrWhiteSpace(_settings.Scanner.ComPort) ? "COM1" : _settings.Scanner.ComPort.Trim();
            _baudRate = _settings.Scanner.BaudRate > 0 ? _settings.Scanner.BaudRate : 115200;
            _powerDbm = Math.Clamp(_settings.Scanner.PowerDbm, 0, 30);
            _dedupMs = Math.Max(0, _settings.Scanner.DedupMs);
            _inventoryMode = string.Equals(_settings.Scanner.InventoryMode, "Triggered", StringComparison.OrdinalIgnoreCase) ? "Triggered" : "Continuous";
            _epcFilterPrefix = (_settings.Scanner.EpcFilterPrefix ?? string.Empty).Trim().ToUpperInvariant();
            RaisePropertyChanged(nameof(Brand));
            RaisePropertyChanged(nameof(Filter));
            RaisePropertyChanged(nameof(AutoTrigger));
            RaisePropertyChanged(nameof(ConnectionType));
            RaisePropertyChanged(nameof(IpAddress));
            RaisePropertyChanged(nameof(TcpPort));
            RaisePropertyChanged(nameof(ComPort));
            RaisePropertyChanged(nameof(BaudRate));
            RaisePropertyChanged(nameof(PowerDbm));
            RaisePropertyChanged(nameof(DedupMs));
            RaisePropertyChanged(nameof(InventoryMode));
            RaisePropertyChanged(nameof(EpcFilterPrefix));
            RaisePropertyChanged(nameof(IsScannerEnabled));
            RaisePropertyChanged(nameof(IsSrk));
            RaisePropertyChanged(nameof(IsLan));
            RaisePropertyChanged(nameof(IsSerial));
            RaisePropertyChanged(nameof(CanTest));
            Status = $"Brand: {_brand}. {(IsScannerEnabled ? "Ready to test." : "Select a brand and save, then Test.")}";
        }

        private ScannerSettings BuildLiveSettings()
        {
            // Use exactly what is on screen (Hercules-matched COM/baud/IP) – not
            // whatever was saved last time. Caller persists it first.
            return new ScannerSettings
            {
                Brand = ScannerSettings.NormalizeBrand(_brand),
                ScannerIdFilter = _filter?.Trim() ?? string.Empty,
                AutoTriggerOnScan = _autoTrigger,
                ConnectionType = ScannerSettings.NormalizeConnection(_connectionType),
                IpAddress = string.IsNullOrWhiteSpace(_ipAddress) ? "192.168.1.250" : _ipAddress.Trim(),
                TcpPort = _tcpPort > 0 ? _tcpPort : 60000,
                ComPort = string.IsNullOrWhiteSpace(_comPort) ? "COM1" : _comPort.Trim(),
                BaudRate = _baudRate > 0 ? _baudRate : 115200,
                PowerDbm = Math.Clamp(_powerDbm, 0, 30),
                InventoryMode = string.Equals(_inventoryMode, "Triggered", StringComparison.OrdinalIgnoreCase) ? "Triggered" : "Continuous",
                DedupMs = Math.Max(0, _dedupMs),
                EpcFilterPrefix = (_epcFilterPrefix ?? string.Empty).Trim().ToUpperInvariant()
            };
        }

        private async Task PersistLiveSettingsAsync()
        {
            // Read-modify-write: reload fresh so unrelated sections saved elsewhere
            // (e.g. Brand theme in App Settings) are never clobbered by a stale snapshot.
            _settings = await _svc.LoadAsync();
            _settings.Scanner = BuildLiveSettings();
            await _svc.SaveAsync(_settings);
        }

        private static void ApplyOverride(object provider, ScannerSettings live)
        {
            if (provider is SrkSymd6lProvider srk) srk.ConfigureOverride(live);
        }

        private static string ProviderError(object provider)
        {
            return provider is SrkSymd6lProvider srk && !string.IsNullOrWhiteSpace(srk.LastError)
                ? " " + srk.LastError : string.Empty;
        }

        private async Task RefreshAsync()
        {
            IsBusy = true;
            try
            {
                Discovered.Clear();
                LastLabel = string.Empty;
                Status = "Saving current settings, then discovering...";
                await PersistLiveSettingsAsync();
                var live = BuildLiveSettings();
                Status = string.Equals(live.ConnectionType, "LAN", StringComparison.OrdinalIgnoreCase)
                    ? $"Discovering... ping {live.IpAddress}:{live.TcpPort} first."
                    : $"Discovering on {live.ComPort}@{live.BaudRate}... close Hercules if it holds the port.";
                var factory = new ScannerProviderFactory();
                var provider = factory.Create(_brand);
                ApplyOverride(provider, live);
                await provider.StartAsync();
                var list = await provider.GetDiscoveredAsync();
                foreach (var d in list) Discovered.Add(d);
                Status = list.Count == 0
                    ? (string.Equals(_brand, "Srk", StringComparison.OrdinalIgnoreCase)
                        ? $"No SRK reader found on {(string.Equals(live.ConnectionType, "LAN", StringComparison.OrdinalIgnoreCase) ? $"{live.IpAddress}:{live.TcpPort}" : $"{live.ComPort}@{live.BaudRate}")}. Check power, {(string.Equals(live.ConnectionType, "LAN", StringComparison.OrdinalIgnoreCase) ? "ping + RJ45 transport" : "Device Manager COM number + 115200 baud")}, close Hercules." + ProviderError(provider)
                        : "No scanners found. Check CoreScanner install and SNAPI mode.")
                    : $"Found {list.Count} scanner(s)";
                await provider.StopAsync();
                await provider.DisposeAsync();
            }
            catch (Exception ex) { Status = $"Discover failed: {ex.Message}"; }
            finally { IsBusy = false; RaisePropertyChanged(nameof(CanTest)); }
        }

        private async Task TestAsync()
        {
            IsBusy = true;
            try
            {
                Status = "Saving current settings, then listening...";
                await PersistLiveSettingsAsync();
                var live = BuildLiveSettings();
                Status = string.Equals(_brand, "Srk", StringComparison.OrdinalIgnoreCase)
                    ? $"Listening for UHF tag on {(string.Equals(live.ConnectionType, "LAN", StringComparison.OrdinalIgnoreCase) ? $"{live.IpAddress}:{live.TcpPort}" : $"{live.ComPort}@{live.BaudRate}")} (present tag now, 15s, ActiveMode)..."
                    : "Listening for barcode (scan now, 10s)...";
                LastLabel = string.Empty;
                var factory = new ScannerProviderFactory();
                var provider = factory.Create(_brand);
                ApplyOverride(provider, live);
                var tcs = new TaskCompletionSource<string>();
                void Handler(object? s, ScannerBarcodeEventArgs e) => tcs.TrySetResult($"{e.Label} [{e.Model} {e.Serial}]");
                provider.BarcodeScanned += Handler;
                await provider.StartAsync();
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(15000));
                provider.BarcodeScanned -= Handler;
                if (completed == tcs.Task)
                {
                    LastLabel = tcs.Task.Result;
                    Status = "Scan OK – selecting this scanner? Set Filter to serial/model and Save.";
                }
                else
                {
                    Status = string.Equals(_brand, "Srk", StringComparison.OrdinalIgnoreCase)
                        ? $"No tag within 15s on {(string.Equals(live.ConnectionType, "LAN", StringComparison.OrdinalIgnoreCase) ? $"{live.IpAddress}:{live.TcpPort}" : $"{live.ComPort}@{live.BaudRate}")} ({live.InventoryMode}). Check tag in field, antenna, power, baud, close Hercules." + ProviderError(provider)
                        : "No barcode within 10s. Check scanner SNAPI mode and trigger.";
                }
                await provider.StopAsync();
                await provider.DisposeAsync();
            }
            catch (Exception ex) { Status = $"Test failed: {ex.Message}"; }
            finally { IsBusy = false; RaisePropertyChanged(nameof(CanTest)); }
        }

        private async Task TriggerAsync()
        {
            IsBusy = true;
            try
            {
                Status = "Saving current settings, then sending trigger...";
                await PersistLiveSettingsAsync();
                var live = BuildLiveSettings();
                var factory = new ScannerProviderFactory();
                var provider = factory.Create(_brand);
                ApplyOverride(provider, live);
                await provider.StartAsync();
                await provider.TriggerAsync();
                Status = "Trigger sent. Scan now or check scanner beep.";
                await Task.Delay(1500);
                await provider.StopAsync();
                await provider.DisposeAsync();
            }
            catch (Exception ex) { Status = $"Trigger failed: {ex.Message}"; }
            finally { IsBusy = false; }
        }

        private async Task SaveAsync()
        {
            try
            {
                // Read-modify-write: reload fresh so unrelated sections saved elsewhere
                // (e.g. Brand theme in App Settings) are never clobbered by a stale snapshot.
                _settings = await _svc.LoadAsync();
                // Persist brand/filter/trigger + SRK connection
                _settings.Scanner.Brand = ScannerSettings.NormalizeBrand(_brand);
                _settings.Scanner.ScannerIdFilter = _filter?.Trim() ?? string.Empty;
                _settings.Scanner.AutoTriggerOnScan = _autoTrigger;
                _settings.Scanner.ConnectionType = ScannerSettings.NormalizeConnection(_connectionType);
                _settings.Scanner.IpAddress = _ipAddress?.Trim() ?? "192.168.1.250";
                _settings.Scanner.TcpPort = _tcpPort > 0 ? _tcpPort : 60000;
                _settings.Scanner.ComPort = string.IsNullOrWhiteSpace(_comPort) ? "COM1" : _comPort.Trim();
                _settings.Scanner.BaudRate = _baudRate > 0 ? _baudRate : 115200;
                _settings.Scanner.PowerDbm = Math.Clamp(_powerDbm, 0, 30);
                _settings.Scanner.DedupMs = Math.Max(0, _dedupMs);
                _settings.Scanner.InventoryMode = string.Equals(_inventoryMode, "Triggered", StringComparison.OrdinalIgnoreCase) ? "Triggered" : "Continuous";
                _settings.Scanner.EpcFilterPrefix = (_epcFilterPrefix ?? string.Empty).Trim().ToUpperInvariant();
                await _svc.SaveAsync(_settings);
                await _audit.LogSettingsChangedAsync(_user, "Scanner Settings", null, $"Brand={_settings.Scanner.Brand}, Filter={_settings.Scanner.ScannerIdFilter}, AutoTrigger={_settings.Scanner.AutoTriggerOnScan}, Conn={_settings.Scanner.ConnectionType}, IP={_settings.Scanner.IpAddress}:{_settings.Scanner.TcpPort}, COM={_settings.Scanner.ComPort}@{_settings.Scanner.BaudRate}, Mode={_settings.Scanner.InventoryMode}");
                // The shell footer picks the saved brand up within a couple of
                // seconds (settings file is re-read by the status timer), so no
                // restart is needed for the status bar or weighing capture.
                var hint = string.Equals(_settings.Scanner.Brand, "Srk", StringComparison.OrdinalIgnoreCase)
                    ? "SRK-SYMD6L over LAN/COM – no CoreScanner needed. Present a tag and use Test."
                    : "Zebra requires CoreScanner in SNAPI mode.";
                MessageBox.Show($"Scanner settings saved.\nBrand: {_settings.Scanner.Brand}\nFilter: {_settings.Scanner.ScannerIdFilter}\nAutoTrigger: {_settings.Scanner.AutoTriggerOnScan}\n\nThe footer scanner status updates automatically. {hint}", "Scanner Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                Status = "Saved – footer scanner status updates automatically.";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}

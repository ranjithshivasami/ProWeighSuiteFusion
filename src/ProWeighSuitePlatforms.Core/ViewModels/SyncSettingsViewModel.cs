using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class SyncSettingsViewModel : BaseViewModel
    {
        public string Title => "Transaction Sync Settings";

        private readonly SyncSettingsService _svc = new();
        private readonly AuditService _auditService = new();
        private readonly string _currentUsername;
        private SyncSettings _settings = new();

        public string ApiBaseUrl
        {
            get => _settings.ApiBaseUrl;
            set { _settings.ApiBaseUrl = value?.Trim() ?? string.Empty; RaisePropertyChanged(nameof(ApiBaseUrl)); }
        }

        public string ApiBaseUrl2
        {
            get => _settings.ApiBaseUrl2;
            set { _settings.ApiBaseUrl2 = value?.Trim() ?? string.Empty; RaisePropertyChanged(nameof(ApiBaseUrl2)); }
        }

        public int RetryCount
        {
            get => _settings.RetryCount;
            set { _settings.RetryCount = value; RaisePropertyChanged(nameof(RetryCount)); }
        }

        public int RetryDelaySeconds
        {
            get => _settings.RetryDelaySeconds;
            set { _settings.RetryDelaySeconds = value; RaisePropertyChanged(nameof(RetryDelaySeconds)); }
        }

        public string TestResponse
        {
            get => _testResponse;
            set => SetProperty(ref _testResponse, value);
        }
        private string _testResponse = string.Empty;

        public bool TestResponseVisible
        {
            get => _testResponseVisible;
            set => SetProperty(ref _testResponseVisible, value);
        }
        private bool _testResponseVisible = false;

        public ObservableCollection<string> AvailableFields { get; } = new();
        public ObservableCollection<string> SelectedFields { get; } = new();
        public ICommand AddFieldCommand { get; }
        public ICommand RemoveFieldCommand { get; }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand TestApiCommand { get; }

        public SyncSettingsViewModel(string currentUsername = "System")
        {
            _currentUsername = currentUsername;
            SaveCommand = new RelayCommand(async _ => await SaveAsync());
            CancelCommand = new RelayCommand(_ => { });
            TestApiCommand = new RelayCommand(
                async _ => await TestApiConnectionAsync(),
                _ => !string.IsNullOrWhiteSpace(ApiBaseUrl));

            AddFieldCommand = new RelayCommand(OnAddField);
            RemoveFieldCommand = new RelayCommand(OnRemoveField);

            PopulateDefaultFields();
            _ = LoadAsync();
        }

        private void OnAddField(object? parameter)
        {
            if (parameter is string key)
            {
                AvailableFields.Remove(key);
                SelectedFields.Add(key);
            }
        }

        private void OnRemoveField(object? parameter)
        {
            if (parameter is string key)
            {
                SelectedFields.Remove(key);
                AvailableFields.Add(key);
            }
        }

        private static readonly List<string> AllFieldKeys = new()
        {
            "transaction_uuid", "batch_number", "sequence",
            "material_code", "material_name", "achived_weight",
            "target_weight", "upper_weight", "lower_weight",
            "weighed_by", "weighed_date_time", "manufacturing_date",
            "expiry_date", "customer_name", "remarks",
            "tare_weight", "equipment_id", "location"
        };

        private static readonly HashSet<string> DefaultFieldKeys = new()
        {
            "transaction_uuid", "batch_number", "material_code",
            "material_name", "achived_weight", "weighed_date_time",
            "equipment_id", "location"
        };

        private void PopulateFromFieldKeys(List<string> fieldKeys)
        {
            AvailableFields.Clear();
            SelectedFields.Clear();
            var selectedSet = new HashSet<string>(fieldKeys);
            foreach (var key in AllFieldKeys)
            {
                if (selectedSet.Contains(key))
                    SelectedFields.Add(key);
                else
                    AvailableFields.Add(key);
            }
        }

        private void PopulateDefaultFields() => PopulateFromFieldKeys(DefaultFieldKeys.ToList());

        public async Task LoadAsync()
        {
            _settings = await _svc.LoadAsync();
            RaisePropertyChanged(nameof(ApiBaseUrl));
            RaisePropertyChanged(nameof(ApiBaseUrl2));
            RaisePropertyChanged(nameof(RetryCount));
            RaisePropertyChanged(nameof(RetryDelaySeconds));

            if (_settings.FieldMappings != null && _settings.FieldMappings.Count > 0)
                PopulateFromFieldKeys(_settings.FieldMappings);
            else
                PopulateDefaultFields();
        }

        public async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(ApiBaseUrl))
            {
                MessageBox.Show(
                    "Primary API Base URL is required.\nPlease enter a valid URL before saving.",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _settings.FieldMappings = SelectedFields.ToList();
            await _svc.SaveAsync(_settings);
            _ = _auditService.LogSyncSettingsSavedAsync(_currentUsername, _settings.ApiBaseUrl, _settings.RetryCount, _settings.RetryDelaySeconds);

            var message = "Sync settings saved successfully!\n\n" +
                "✓ Primary API URL: Determines sync success/failure in Qumonitor\n";

            if (!string.IsNullOrWhiteSpace(_settings.ApiBaseUrl2))
            {
                message += "✓ Secondary API URL: For internal testing only (does not affect sync status)\n";
            }

            message += "\nChanges take effect immediately for new transactions.\n\n" +
                "If you have pending items that were failing with the old URL:\n" +
                "1. Open Queue Monitor\n" +
                "2. Click 'Clear All' to remove old failed items\n" +
                "3. Re-do the weighing transactions\n\n" +
                "Click OK to continue.";

            MessageBox.Show(
                message,
                "Settings Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async Task TestApiConnectionAsync()
        {
            if (string.IsNullOrWhiteSpace(ApiBaseUrl))
            {
                TestResponse = "Error: Primary API Base URL is required.";
                TestResponseVisible = true;
                return;
            }

            var urlsToTest = new List<(string Label, string Url)>
            {
                ("Primary API URL (Determines Sync Status)", ApiBaseUrl.TrimEnd('/'))
            };

            if (!string.IsNullOrWhiteSpace(ApiBaseUrl2))
                urlsToTest.Add(("Secondary API URL (Internal Testing Only)", ApiBaseUrl2.TrimEnd('/')));

            try
            {
                TestResponse = "?? Testing connection(s)...\n\nSending sample transaction data...";
                TestResponseVisible = true;

                // Build sample transaction
                var sampleTransaction = new WeighingTransaction
                {
                    Id = "00000000-0000-0000-0000-000000000999",
                    BatchNumber = "TEST-BATCH-001",
                    BatchId = 1,
                    MaterialCode = "MAT-TEST-001",
                    MaterialName = "Test Material",
                    Sequence = 1,
                    TargetWeight = 100.0,
                    LowerWeight = 95.0,
                    UpperWeight = 105.0,
                    ActualWeight = 98.5,
                    Deviation = -1.5,
                    Status = "Accept",
                    WeighedBy = "TestUser",
                    WeighedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    PrintedDate = string.Empty,
                    CustomerName = "Test Customer",
                    ManufacturingDate = DateTime.Now.ToString("yyyy-MM-dd"),
                    ExpiryDate = DateTime.Now.AddYears(1).ToString("yyyy-MM-dd"),
                    EquipmentId = "SCALE-001",
                    Location = "Test Location",
                    Remarks = "This is a test transaction",
                    TareWeight = 0.5
                };

                var payload = TransactionSyncPayload.FromTransaction(sampleTransaction);
                var json = SelectedFields.Count > 0
                    ? payload.ToFilteredJson(SelectedFields.ToList())
                    : JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = null
                    });

                TestResponse += $"\n\n?? Posted Data (first 500 chars):\n{json.Substring(0, Math.Min(500, json.Length))}{(json.Length > 500 ? "\n... (truncated)" : "")}\n";

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var resultBuilder = new StringBuilder();

                foreach (var (label, url) in urlsToTest)
                {
                    resultBuilder.AppendLine();
                    resultBuilder.AppendLine($"{'=',-60}");
                    resultBuilder.AppendLine($"?? {label}: {url}");
                    resultBuilder.AppendLine($"{'=',-60}");

                    try
                    {
                        using var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var response = await httpClient.PostAsync(url, content);
                        var responseBody = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            resultBuilder.AppendLine($"? Success! HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                            resultBuilder.AppendLine($"?? Response:\n{responseBody}");
                        }
                        else
                        {
                            resultBuilder.AppendLine($"? Failed! HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                            resultBuilder.AppendLine($"Response:\n{responseBody}");
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        resultBuilder.AppendLine($"? Network Error: {ex.Message}");
                        resultBuilder.AppendLine("Check that the URL is correct and the server is running.");
                    }
                    catch (TaskCanceledException)
                    {
                        resultBuilder.AppendLine("?? Request Timeout: The server took too long to respond (>10 seconds).");
                    }
                    catch (Exception ex)
                    {
                        resultBuilder.AppendLine($"? Unexpected Error: {ex.Message}");
                    }
                }

                TestResponse += resultBuilder.ToString();
            }
            catch (Exception ex)
            {
                TestResponse = $"? Unexpected Error:\n{ex.Message}";
            }
            finally
            {
                TestResponseVisible = true;
            }
        }
    }
}

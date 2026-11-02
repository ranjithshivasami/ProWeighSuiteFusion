using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using ProWeighSuitePlatforms.Data;
using System.Diagnostics;

namespace ProWeighSuitePlatforms.Services
{
    public class TransactionSyncService
    {
        private readonly HttpClient _httpClient;
        private readonly TransactionStagingRepository _staging;
        private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        public TransactionSyncService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            // Use same DB path as other repositories
            var dbPath = ProWeighSuitePlatforms.Configuration.AppConfigurationManager.GetDatabasePath();
            _staging = new TransactionStagingRepository(dbPath);

            // Simple retry policy with jitter
            _retryPolicy = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(r => !r.IsSuccessStatusCode)
                .WaitAndRetryAsync(new[] {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(8)
                }, onRetry: (outcome, timespan, retryCount, context) =>
                {
                    Debug.WriteLine($"[TransactionSyncService] Retry {retryCount} sending transaction: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                });
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_backgroundTask != null) return; // already started

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _cts.Token;

            await _staging.InitializeAsync();

            _backgroundTask = Task.Run(() => RunAsync(token), CancellationToken.None);
            Debug.WriteLine($"[TransactionSyncService] Background sync worker started. API Base: {_httpClient.BaseAddress}");
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;

            try
            {
                _cts.Cancel();
                if (_backgroundTask != null)
                    await _backgroundTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TransactionSyncService] Stop error: {ex}");
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _backgroundTask = null;
            }
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            Debug.WriteLine("[TransactionSyncService] Sync loop started. Checking every 10 seconds...");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var pending = await _staging.GetPendingAsync(limit: 20);
                    if (pending.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                        continue;
                    }

                    Debug.WriteLine($"[TransactionSyncService] Found {pending.Count} pending transaction(s) to sync");

                    foreach (var item in pending)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        try
                        {
                            await _staging.MarkProcessingAsync(item.Id);

                            Debug.WriteLine($"[TransactionSyncService] Sending transaction {item.TransactionId} to {_httpClient.BaseAddress}/index.php");

                            // ? FIX: Create request factory to avoid reusing HttpRequestMessage
                            var response = await _retryPolicy.ExecuteAsync(async () =>
                            {
                                // Create NEW request for each attempt (including retries)
                                using var request = new HttpRequestMessage(HttpMethod.Post, "/index.php")
                                {
                                    Content = new StringContent(item.PayloadJson ?? string.Empty, Encoding.UTF8, "application/json")
                                };

                                // Add idempotency header
                                if (!string.IsNullOrEmpty(item.IdempotencyKey))
                                    request.Headers.Add("Idempotency-Key", item.IdempotencyKey);

                                return await _httpClient.SendAsync(request, stoppingToken);
                            });

                            if (response.IsSuccessStatusCode)
                            {
                                await _staging.MarkSyncedAsync(item.Id);
                                Debug.WriteLine($"[TransactionSyncService] ? Successfully synced transaction {item.TransactionId}");
                            }
                            else
                            {
                                var responseBody = await response.Content.ReadAsStringAsync();
                                var err = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}";
                                await _staging.MarkFailedAsync(item.Id, err, item.Attempts + 1, retryAfter: TimeSpan.FromSeconds(30));
                                Debug.WriteLine($"[TransactionSyncService] ? Failed to sync transaction {item.TransactionId}: {err}");
                            }
                        }
                        catch (Exception ex)
                        {
                            await _staging.MarkFailedAsync(item.Id, ex.Message, item.Attempts + 1, retryAfter: TimeSpan.FromSeconds(30));
                            Debug.WriteLine($"[TransactionSyncService] ? Error syncing transaction {item.TransactionId}: {ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TransactionSyncService] Top-level error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
            
            Debug.WriteLine("[TransactionSyncService] Sync loop stopped");
        }
    }
}

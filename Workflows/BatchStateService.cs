using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Persists and restores active batch state to a local JSON file so that
    /// an in-progress batch can survive application restarts.
    /// </summary>
    public class BatchStateService
    {
        private readonly string _stateFilePath;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        public BatchStateService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuiteFusion");

            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);

            _stateFilePath = Path.Combine(appDataPath, "active-batch-state.json");
        }

        private readonly object _fileLock = new();

        /// <summary>
        /// Save the current batch state to disk atomically (temp file + move) to avoid truncated JSON on crash.
        /// </summary>
        public Task SaveStateAsync(BatchState state)
        {
            try
            {
                var json = JsonSerializer.Serialize(state, _jsonOptions);
                var tmpPath = _stateFilePath + ".tmp";
                // Lock to avoid concurrent writes from PLC/AcceptWeight calling SaveStateAsync rapidly
                lock (_fileLock)
                {
                    // synchronous write inside lock but safe for 1-2KB file
                    File.WriteAllText(tmpPath, json);
                    // Atomic replace — on Windows Move overwrites
                    if (File.Exists(_stateFilePath))
                        File.Delete(_stateFilePath);
                    File.Move(tmpPath, _stateFilePath);
                }
                Debug.WriteLine($"[BatchStateService] State saved for batch {state.BatchNumber}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BatchStateService] Failed to save state: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Load a previously saved batch state, or null if none exists.
        /// Handles corrupted/truncated files by deleting them.
        /// </summary>
        public Task<BatchState?> LoadStateAsync()
        {
            try
            {
                if (!File.Exists(_stateFilePath))
                    return Task.FromResult<BatchState?>(null);

                string json;
                lock (_fileLock)
                {
                    json = File.ReadAllText(_stateFilePath);
                }
                if (string.IsNullOrWhiteSpace(json))
                {
                    ClearState();
                    return Task.FromResult<BatchState?>(null);
                }
                var state = JsonSerializer.Deserialize<BatchState>(json, _jsonOptions);
                if (state == null || state.BatchId <= 0 || string.IsNullOrWhiteSpace(state.BatchNumber))
                {
                    Debug.WriteLine("[BatchStateService] Loaded state invalid — clearing");
                    ClearState();
                    return Task.FromResult<BatchState?>(null);
                }
                Debug.WriteLine($"[BatchStateService] State loaded for batch {state?.BatchNumber}");
                return Task.FromResult<BatchState?>(state);
            }
            catch (JsonException jex)
            {
                Debug.WriteLine($"[BatchStateService] Corrupted JSON, deleting file: {jex.Message}");
                try { ClearState(); } catch { }
                return Task.FromResult<BatchState?>(null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BatchStateService] Failed to load state: {ex.Message}");
                return Task.FromResult<BatchState?>(null);
            }
        }

        /// <summary>
        /// Delete the saved state file (called when a batch completes, is stopped, or aborted).
        /// </summary>
        public void ClearState()
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    File.Delete(_stateFilePath);
                    Debug.WriteLine("[BatchStateService] State file cleared");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BatchStateService] Failed to clear state: {ex.Message}");
            }
        }

        /// <summary>
        /// Check whether a saved state file exists.
        /// </summary>
        public bool HasSavedState()
        {
            return File.Exists(_stateFilePath);
        }
    }
}

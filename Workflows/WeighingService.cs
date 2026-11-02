using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class WeighingService
    {
        private readonly WeighingRepository _repository;

        public WeighingService(WeighingRepository repository)
        {
            _repository = repository;
        }

        public async Task InitializeAsync()
        {
            await _repository.InitializeAsync();
        }

        public async Task<int> GetNextBatchSequenceForTodayAsync()
        {
            return await _repository.GetNextBatchSequenceForTodayAsync();
        }

        public async Task<string> GetLastTransactionIdAsync()
        {
            return await _repository.GetLastTransactionIdAsync();
        }

        public async Task<string> GetLastTransactionBatchNumberAsync()
        {
            return await _repository.GetLastTransactionBatchNumberAsync();
        }

        public async Task<int> CreateBatchAsync(WeighingBatch batch)
        {
            return await _repository.CreateBatchAsync(batch);
        }

        public async Task<WeighingBatch?> GetActiveBatchAsync(int batchId)
        {
            return await _repository.GetActiveBatchAsync(batchId);
        }

        public async Task<string> CreateTransactionAsync(WeighingTransaction transaction)
        {
            return await _repository.CreateTransactionAsync(transaction);
        }

        public async Task MarkTransactionPrintedAsync(string transactionId)
        {
            await _repository.UpdateTransactionPrintedAsync(transactionId);
        }

        public async Task CompleteBatchAsync(int batchId)
        {
            await _repository.UpdateBatchStatusAsync(batchId, "Completed", DateTime.Now);
        }

        public async Task PauseBatchAsync(int batchId)
        {
            await _repository.UpdateBatchStatusAsync(batchId, "Paused");
        }

        public async Task AbortBatchAsync(int batchId)
        {
            await _repository.UpdateBatchStatusAsync(batchId, "Aborted", DateTime.Now);
        }

        public async Task<bool> HasMaterialBeenWeighedAsync(int batchId, string materialCode)
        {
            return await _repository.HasMaterialBeenWeighedAsync(batchId, materialCode);
        }

        public async Task<List<WeighingTransaction>> GetBatchTransactionsAsync(int batchId)
        {
            return await _repository.GetBatchTransactionsAsync(batchId);
        }

        public async Task UpdateTransactionBatchNumberAsync(string transactionId, string batchNumber)
        {
            await _repository.UpdateTransactionBatchNumberAsync(transactionId, batchNumber);
        }

        public async Task UpdateTransactionSyncStatusAsync(string transactionId, bool isSynced)
        {
            await _repository.UpdateTransactionSyncStatusAsync(transactionId, isSynced);
        }

        public async Task<List<WeighingTransaction>> GetRecentTransactionsAsync(int limit = 100)
        {
            return await _repository.GetRecentTransactionsAsync(limit);
        }

        public async Task<List<WeighingTransaction>> GetRecentTransactionsForTodayAsync(int limit = 6)
        {
            return await _repository.GetRecentTransactionsForTodayAsync(limit);
        }

        public async Task<(int Total, int Synced, int Unsynced)> GetSyncCountsAsync()
        {
            return await _repository.GetSyncCountsAsync();
        }

        public async Task<WeighingTransaction?> GetTransactionByIdAsync(string transactionId)
        {
            return await _repository.GetTransactionByIdAsync(transactionId);
        }

        public async Task<List<WeighingTransaction>> GetTransactionsByBatchNumberAsync(string batchNumber)
        {
            return await _repository.GetTransactionsByBatchNumberAsync(batchNumber);
        }

        public async Task<int> SaveReprintRecordAsync(ReprintRecord record)
        {
            return await _repository.SaveReprintRecordAsync(record);
        }

        // Update current batch progress (number of transactions completed)
        public async Task UpdateBatchCurrentNumberAsync(int batchId, int currentNumber)
        {
            await _repository.UpdateBatchCurrentNumberAsync(batchId, currentNumber);
        }

        private string GenerateBatchNumber(string scaleNumber)
        {
            // Format: ScaleNumber-YYYYMMDD-BatchSeq
            var now = DateTime.Now;
            return $"{scaleNumber}-{now:yyyyMMdd}-1";
        }
    }
}

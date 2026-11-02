using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Platform.Weighing.Contracts;

public interface ITransactionRetryQueue
{
    bool IsTransactionFailed(string transactionId);
    void EnqueueTransaction(WeighingTransaction transaction);
    void AdjustFailureCountForRetry(int retriedCount);
    int GetPendingCount();
    (int Success, int Failures, int Pending) GetStats();
    string GetQueueDebugInfo();
    void ClearQueue();
    void ClearAllQueueData();
    void RemoveSuccessfulItems();
    void ForceCleanupAllData();
    (int PendingCleared, int FailedCleared, int SuccessCleared) ResetPendingAndFailed();
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

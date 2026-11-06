// Fusion "Batch status" screen: every batch as a paged DataGrid with a date-range
// filter (defaults to today). Each batch row has a View button that opens a large
// popup showing the batch's lot details.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class BatchStatusViewModel : BaseViewModel
    {
        public string Title => "Batch status";

        public ObservableCollection<FusionBatch> Batches { get; } = new();
        public ObservableCollection<FusionBatch> PagedBatches { get; } = new();
        public ObservableCollection<FusionPlanLot> Lots { get; } = new();
        public ObservableCollection<PageNumberItem> PageNumbers { get; } = new();

        // ---- Date range filter ----
        private DateTime? _fromDate = DateTime.Today;
        public DateTime? FromDate
        {
            get => _fromDate;
            set
            {
                if (SetProperty(ref _fromDate, value))
                {
                    CurrentPage = 1;
                    _ = LoadAsync();
                }
            }
        }

        private DateTime? _toDate = DateTime.Today;
        public DateTime? ToDate
        {
            get => _toDate;
            set
            {
                if (SetProperty(ref _toDate, value))
                {
                    CurrentPage = 1;
                    _ = LoadAsync();
                }
            }
        }

        // ---- Pagination ----
        private int _pageSize = 10;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    CurrentPage = 1;
                    UpdatePagedData();
                }
            }
        }

        private int _currentPage = 1;
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    UpdatePagedData();
                    RaisePropertyChanged(nameof(TotalPages));
                    RaisePropertyChanged(nameof(PageInfo));
                    RaisePropertyChanged(nameof(ShowingText));
                }
            }
        }

        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalRecords / Math.Max(1, PageSize)));

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            private set => SetProperty(ref _totalRecords, value);
        }

        public string PageInfo => $"Page {CurrentPage} of {TotalPages}  ({TotalRecords} records)";

        public string ShowingText
        {
            get
            {
                if (TotalRecords == 0) return "Showing 0 to 0 of 0 records";
                var from = (CurrentPage - 1) * PageSize + 1;
                var to = Math.Min(CurrentPage * PageSize, TotalRecords);
                return $"Showing {from} to {to} of {TotalRecords} records";
            }
        }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public ICommand RefreshCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand GoToPageCommand { get; }
        public ICommand ViewBatchCommand { get; }

        public BatchStatusViewModel()
        {
            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            PrevPageCommand = new RelayCommand(_ => { if (CurrentPage > 1) CurrentPage--; }, _ => CurrentPage > 1);
            NextPageCommand = new RelayCommand(_ => { if (CurrentPage < TotalPages) CurrentPage++; }, _ => CurrentPage < TotalPages);
            GoToPageCommand = new RelayCommand(param =>
            {
                if (param is int page && page >= 1 && page <= Math.Max(1, TotalPages) && page != CurrentPage)
                    CurrentPage = page;
            });
            ViewBatchCommand = new RelayCommand(async param => await ViewBatchAsync(param as FusionBatch));
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());

                var batches = await repo.GetBatchesAsync(FromDate, ToDate);

                Batches.Clear();
                foreach (var b in batches)
                    Batches.Add(b);

                TotalRecords = Batches.Count;

                if (CurrentPage > TotalPages) CurrentPage = 1;
                UpdatePagedData();

                StatusText = Batches.Count == 0
                    ? "No batches found for the selected date range."
                    : $"{Batches.Count} batch(es) listed.";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        private void UpdatePagedData()
        {
            var paged = Batches
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();
            PagedBatches.Clear();
            foreach (var b in paged)
                PagedBatches.Add(b);

            RefreshPageNumbers();
            RaisePropertyChanged(nameof(CurrentPage));
            RaisePropertyChanged(nameof(TotalPages));
            RaisePropertyChanged(nameof(PageInfo));
            RaisePropertyChanged(nameof(ShowingText));
        }

        private void RefreshPageNumbers()
        {
            PageNumbers.Clear();
            var total = Math.Max(1, TotalPages);
            var start = Math.Max(1, Math.Min(CurrentPage - 2, total - 4));
            var end = Math.Min(total, start + 4);
            start = Math.Max(1, end - 4);
            for (var i = start; i <= end; i++)
                PageNumbers.Add(new PageNumberItem { Number = i, IsCurrent = i == CurrentPage });
        }

        // ---- Lot detail popup ----
        private string? _popupBatchNo;
        public string LotPopupTitle => string.IsNullOrEmpty(_popupBatchNo)
            ? "Lot details"
            : $"Lot details - {_popupBatchNo}";

        private async Task ViewBatchAsync(FusionBatch? batch)
        {
            if (batch == null) return;

            _popupBatchNo = batch.BatchNo;
            RaisePropertyChanged(nameof(LotPopupTitle));

            await LoadLotsAsync(batch.BatchNo);

            var window = new Views.BatchLotDetailWindow
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = this,
            };
            window.ShowDialog();
        }

        private async Task LoadLotsAsync(string? batchNo)
        {
            Lots.Clear();
            if (string.IsNullOrWhiteSpace(batchNo)) return;

            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                foreach (var lot in await repo.GetPlanLotsAsync(batchNo))
                    Lots.Add(lot);
            }
            catch (Exception ex)
            {
                StatusText = $"Lot load failed: {ex.Message}";
            }
        }

        public sealed class PageNumberItem
        {
            public int Number { get; set; }
            public bool IsCurrent { get; set; }
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class ZplTemplateViewModel : BaseViewModel
    {
        private readonly ZplTemplateService _service;
        private readonly AuditService _auditService = new();
        private readonly string _currentUsername;

        public ObservableCollection<ZplLabelTemplate> Templates { get; } = new();
        public ObservableCollection<ZplLabelTemplate> PagedTemplates { get; } = new();

        // Available placeholders for the reference panel
        public ObservableCollection<PlaceholderItem> Placeholders { get; } = new();

        // Available DPI options
        public ObservableCollection<int> DpiOptions { get; } = new() { 152, 203, 300, 600 };

        public string Title => "ZPL Label Templates";

        // -- Commands -------------------------------------------------------
        public ICommand RefreshCommand { get; }
        public ICommand NewTemplateCommand { get; }
        public ICommand EditTemplateCommand { get; }
        public ICommand DeleteTemplateCommand { get; }
        public ICommand SetActiveCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand TestPrintCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand LastPageCommand { get; }

        // -- Filter & Pagination --------------------------------------------
        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    CurrentPage = 1;
                    ApplyFilterAndPage();
                }
            }
        }

        private int _pageSize = 10;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    CurrentPage = 1;
                    ApplyFilterAndPage();
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
                    ApplyFilterAndPage();
                    RaisePropertyChanged(nameof(PageInfo));
                }
            }
        }

        private int _totalPages = 1;
        public int TotalPages
        {
            get => _totalPages;
            private set
            {
                if (SetProperty(ref _totalPages, value))
                    RaisePropertyChanged(nameof(PageInfo));
            }
        }

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            private set
            {
                if (SetProperty(ref _totalRecords, value))
                    RaisePropertyChanged(nameof(PageInfo));
            }
        }

        public string PageInfo => $"Page {CurrentPage} of {TotalPages}  ({TotalRecords} records)";

        // -- List state -----------------------------------------------------
        private ZplLabelTemplate? _selectedTemplate;
        public ZplLabelTemplate? SelectedTemplate
        {
            get => _selectedTemplate;
            set => SetProperty(ref _selectedTemplate, value);
        }

        // -- Editor state ---------------------------------------------------
        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        private bool _isNew;
        public bool IsNew
        {
            get => _isNew;
            set => SetProperty(ref _isNew, value);
        }

        private int _editId;

        private string _editName = string.Empty;
        public string EditName
        {
            get => _editName;
            set => SetProperty(ref _editName, value?.Trim() ?? string.Empty);
        }

        private string _editDescription = string.Empty;
        public string EditDescription
        {
            get => _editDescription;
            set => SetProperty(ref _editDescription, value?.Trim() ?? string.Empty);
        }

        private string _editZplCode = string.Empty;
        public string EditZplCode
        {
            get => _editZplCode;
            set => SetProperty(ref _editZplCode, value);
        }

        private int _editWidthMm = 100;
        public int EditWidthMm
        {
            get => _editWidthMm;
            set => SetProperty(ref _editWidthMm, value);
        }

        private int _editHeightMm = 50;
        public int EditHeightMm
        {
            get => _editHeightMm;
            set => SetProperty(ref _editHeightMm, value);
        }

        private int _editDpi = 203;
        public int EditDpi
        {
            get => _editDpi;
            set => SetProperty(ref _editDpi, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // -- Preview --------------------------------------------------------
        private string _previewZpl = string.Empty;
        public string PreviewZpl
        {
            get => _previewZpl;
            set => SetProperty(ref _previewZpl, value);
        }

        private byte[]? _previewImage;
        public byte[]? PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }

        private bool _isPreviewLoading;
        public bool IsPreviewLoading
        {
            get => _isPreviewLoading;
            set => SetProperty(ref _isPreviewLoading, value);
        }

        // -- Constructor ----------------------------------------------------
        public ZplTemplateViewModel(string currentUsername = "System")
        {
            _currentUsername = currentUsername;
            _service = new ZplTemplateService();

            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            NewTemplateCommand = new RelayCommand(_ => StartNew());
            EditTemplateCommand = new RelayCommand(p => StartEdit(p as ZplLabelTemplate));
            DeleteTemplateCommand = new RelayCommand(async p => await DeleteAsync(p as ZplLabelTemplate));
            SetActiveCommand = new RelayCommand(async p => await SetActiveAsync(p as ZplLabelTemplate));
            SaveCommand = new RelayCommand(async _ => await SaveAsync());
            CancelCommand = new RelayCommand(_ => CancelEdit());
            PreviewCommand = new RelayCommand(async _ => await PreviewAsync());
            TestPrintCommand = new RelayCommand(async _ => await TestPrintAsync());

            NextPageCommand = new RelayCommand(_ => { if (CurrentPage < TotalPages) CurrentPage++; });
            PrevPageCommand = new RelayCommand(_ => { if (CurrentPage > 1) CurrentPage--; });
            FirstPageCommand = new RelayCommand(_ => { if (CurrentPage != 1) CurrentPage = 1; });
            LastPageCommand = new RelayCommand(_ => { if (CurrentPage != TotalPages) CurrentPage = TotalPages; });

            // Load placeholder reference
            foreach (var (token, desc) in ZplTemplateService.GetAvailablePlaceholders())
            {
                Placeholders.Add(new PlaceholderItem { Token = token, Description = desc });
            }

            _ = InitAndLoadAsync();
        }

        // -- Data loading ---------------------------------------------------
        private async Task InitAndLoadAsync()
        {
            try
            {
                await _service.InitializeAsync();
                await _service.SeedDefaultTemplateIfEmptyAsync();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async Task LoadAsync()
        {
            try
            {
                var list = await _service.GetAllAsync();
                Templates.Clear();
                foreach (var t in list)
                    Templates.Add(t);

                CurrentPage = 1;
                ApplyFilterAndPage();

                StatusMessage = $"{Templates.Count} template(s) loaded.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading templates: {ex.Message}";
            }
        }

        private void ApplyFilterAndPage()
        {
            var query = Templates.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                var ft = FilterText.ToLowerInvariant();
                query = query.Where(t =>
                    (!string.IsNullOrEmpty(t.Name) && t.Name.ToLowerInvariant().Contains(ft)) ||
                    (!string.IsNullOrEmpty(t.Description) && t.Description.ToLowerInvariant().Contains(ft)));
            }

            var filtered = query.ToList();
            TotalRecords = filtered.Count;
            TotalPages = TotalRecords == 0 ? 1 : (int)Math.Ceiling(TotalRecords / (double)PageSize);
            if (CurrentPage > TotalPages) _currentPage = TotalPages;
            RaisePropertyChanged(nameof(PageInfo));

            var page = filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();

            PagedTemplates.Clear();
            var index = (CurrentPage - 1) * PageSize + 1;
            foreach (var t in page)
            {
                t.SerialNumber = index++;
                PagedTemplates.Add(t);
            }

            if (PagedTemplates.Count > 0)
                SelectedTemplate = PagedTemplates[0];
        }

        // -- New ------------------------------------------------------------
        private void StartNew()
        {
            _editId = 0;
            IsNew = true;
            EditName = string.Empty;
            EditDescription = string.Empty;
            EditZplCode = ZplTemplateService.GetDefaultZplTemplate();
            EditWidthMm = 100;
            EditHeightMm = 50;
            EditDpi = 203;
            PreviewZpl = string.Empty;
            PreviewImage = null;
            IsEditing = true;
            StatusMessage = "Creating new template. Paste your ZPL code and map placeholders.";
        }

        // -- Edit -----------------------------------------------------------
        private void StartEdit(ZplLabelTemplate? template)
        {
            if (template == null) return;

            _editId = template.Id;
            IsNew = false;
            EditName = template.Name;
            EditDescription = template.Description;
            EditZplCode = template.ZplCode;
            EditWidthMm = template.LabelWidthMm;
            EditHeightMm = template.LabelHeightMm;
            EditDpi = template.Dpi;
            PreviewZpl = string.Empty;
            PreviewImage = null;
            IsEditing = true;
            StatusMessage = $"Editing template: {template.Name}";
        }

        // -- Save -----------------------------------------------------------
        private async Task SaveAsync()
        {
            try
            {
                var (isValid, error) = ZplTemplateService.ValidateTemplate(EditName, EditZplCode);
                if (!isValid)
                {
                    MessageBox.Show(error, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Check duplicate name
                if (await _service.NameExistsAsync(EditName, IsNew ? null : _editId))
                {
                    MessageBox.Show("A template with this name already exists.", "Duplicate Name",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (IsNew)
                {
                    var template = new ZplLabelTemplate
                    {
                        Name = EditName,
                        Description = EditDescription,
                        ZplCode = EditZplCode,
                        LabelWidthMm = EditWidthMm,
                        LabelHeightMm = EditHeightMm,
                        Dpi = EditDpi,
                        IsActive = Templates.Count == 0,
                        CreatedAt = DateTime.Now
                    };
                    await _service.CreateAsync(template);
                    _ = _auditService.LogZplTemplateCreatedAsync(_currentUsername, template);
                    StatusMessage = $"Template '{EditName}' created.";
                }
                else
                {
                    var existing = await _service.GetByIdAsync(_editId);
                    if (existing == null)
                    {
                        MessageBox.Show("Template not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    existing.Name = EditName;
                    existing.Description = EditDescription;
                    existing.ZplCode = EditZplCode;
                    existing.LabelWidthMm = EditWidthMm;
                    existing.LabelHeightMm = EditHeightMm;
                    existing.Dpi = EditDpi;
                    await _service.UpdateAsync(existing);
                    _ = _auditService.LogZplTemplateUpdatedAsync(_currentUsername, existing);
                    StatusMessage = $"Template '{EditName}' updated.";
                }

                IsEditing = false;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _ = _auditService.LogErrorAsync(_currentUsername, "Save ZPL template", ex);
                MessageBox.Show($"Error saving template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -- Cancel ---------------------------------------------------------
        private void CancelEdit()
        {
            IsEditing = false;
            PreviewZpl = string.Empty;
            PreviewImage = null;
            StatusMessage = string.Empty;
        }

        // -- Delete ---------------------------------------------------------
        private async Task DeleteAsync(ZplLabelTemplate? template)
        {
            if (template == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete template '{template.Name}'?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _service.DeleteAsync(template.Id);
                _ = _auditService.LogZplTemplateDeletedAsync(_currentUsername, template);
                StatusMessage = $"Template '{template.Name}' deleted.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _ = _auditService.LogErrorAsync(_currentUsername, "Delete ZPL template", ex);
                MessageBox.Show($"Error deleting template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -- Set Active -----------------------------------------------------
        private async Task SetActiveAsync(ZplLabelTemplate? template)
        {
            if (template == null) return;

            try
            {
                await _service.SetActiveAsync(template.Id);
                _ = _auditService.LogZplTemplateSetActiveAsync(_currentUsername, template);
                StatusMessage = $"Template '{template.Name}' set as active.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _ = _auditService.LogErrorAsync(_currentUsername, "Set active ZPL template", ex);
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -- Preview --------------------------------------------------------
        private async Task PreviewAsync()
        {
            try
            {
                IsPreviewLoading = true;
                PreviewImage = null;

                var sample = ZplTemplateService.CreateSampleTransaction();
                var resolved = ZplTemplateService.ResolveZpl(EditZplCode, sample);
                PreviewZpl = resolved;

                // Try Labelary API for image preview
                var dpmm = EditDpi switch
                {
                    152 => 6, 203 => 8, 300 => 12, 600 => 24, _ => 8
                };
                var widthInches = EditWidthMm / 25.4;
                var heightInches = EditHeightMm / 25.4;
                var url = $"http://api.labelary.com/v1/printers/{dpmm}dpmm/labels/{widthInches:F2}x{heightInches:F2}/0/";

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var content = new StringContent(resolved, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
                http.DefaultRequestHeaders.Accept.Clear();
                http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("image/png"));

                var response = await http.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    PreviewImage = await response.Content.ReadAsByteArrayAsync();
                    StatusMessage = "Preview generated via Labelary API.";
                }
                else
                {
                    StatusMessage = $"Labelary API returned {response.StatusCode}. See resolved ZPL below.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Preview image unavailable ({ex.Message}). See resolved ZPL below.";
            }
            finally
            {
                IsPreviewLoading = false;
            }
        }

        // -- Test Print ------------------------------------------------------
        private async Task TestPrintAsync()
        {
            try
            {
                var sample = ZplTemplateService.CreateSampleTransaction();
                var resolved = ZplTemplateService.ResolveZpl(EditZplCode, sample);

                var printerName = GetZplPrinterName();
                if (!string.IsNullOrEmpty(printerName))
                {
                    await Task.Run(() => RawPrinterHelper.SendStringToPrinter(printerName, resolved));
                    _ = _auditService.LogPrinterTestPrintAsync(_currentUsername, printerName, true);
                    StatusMessage = $"Test label sent to printer '{printerName}'.";
                }
                else
                {
                    StatusMessage = "No ZPL-compatible printer found.";
                }
            }
            catch (Exception ex)
            {
                _ = _auditService.LogErrorAsync(_currentUsername, "Test print ZPL template", ex);
                StatusMessage = $"Test print failed: {ex.Message}";
            }
        }

        private static string GetZplPrinterName()
        {
            foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                var lower = name.ToLower();
                if (lower.Contains("zebra") || lower.Contains("zpl") || lower.Contains("label"))
                    return name;
            }
            return new System.Drawing.Printing.PrinterSettings().PrinterName;
        }
    }

    /// <summary>
    /// Simple item for placeholder reference panel.
    /// </summary>
    public class PlaceholderItem
    {
        public string Token { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}

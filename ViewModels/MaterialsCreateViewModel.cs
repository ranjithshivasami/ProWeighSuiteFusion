using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Views;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class MaterialsCreateViewModel : BaseViewModel
    {
        private readonly MaterialsViewModel _parentViewModel;
        private readonly MaterialService _materialService;
        private readonly AuditService _auditService;
        private readonly Material? _editingMaterial;
        private readonly ObservableCollection<Category> _categories;
        private readonly ObservableCollection<Variant> _variants;
        private readonly string _currentUsername;
        private readonly AppSettingsService _appSettingsService = new();
        private AppSettings _appSettings = new();

        public string Title => _editingMaterial == null ? "Create New Product" : "Update Product";
        public string SubmitButtonText => _editingMaterial == null ? "Create" : "Update";

        private string _productName = string.Empty;
        public string ProductName
        {
            get => _productName;
            set
            {
                if (SetProperty(ref _productName, value))
                {
                    ProductNameError = string.Empty;
                }
            }
        }

        private string _productCode = string.Empty;
        public string ProductCode
        {
            get => _productCode;
            set
            {
                if (SetProperty(ref _productCode, value?.Trim() ?? string.Empty))
                {
                    ProductCodeError = string.Empty;
                }
            }
        }

        private Category? _selectedCategory;
        public Category? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    CategoryError = string.Empty;
                }
            }
        }

        private Variant? _selectedVariant;
        public Variant? SelectedVariant
        {
            get => _selectedVariant;
            set
            {
                if (SetProperty(ref _selectedVariant, value))
                {
                    VariantError = string.Empty;
                }
            }
        }

        private bool _isActive = true;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        // Optional material image (cropped). Stored as file path under %AppData%\ProWeighSuitePlatforms\MaterialImages
        private string? _imagePath;
        public string? ImagePath
        {
            get => _imagePath;
            set
            {
                if (SetProperty(ref _imagePath, value))
                {
                    RaisePropertyChanged(nameof(HasImage));
                    LoadImagePreview();
                }
            }
        }

        public bool HasImage => !string.IsNullOrWhiteSpace(_imagePath) && File.Exists(_imagePath);

        private BitmapImage? _imagePreview;
        public BitmapImage? ImagePreview
        {
            get => _imagePreview;
            set => SetProperty(ref _imagePreview, value);
        }

        private void LoadImagePreview()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_imagePath) || !File.Exists(_imagePath))
                {
                    ImagePreview = null;
                    return;
                }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(_imagePath!, UriKind.Absolute);
                bmp.DecodePixelWidth = 300;
                bmp.EndInit();
                bmp.Freeze();
                ImagePreview = bmp;
            }
            catch
            {
                ImagePreview = null;
            }
        }

        private string _shelfLife = "365";
        public string ShelfLife
        {
            get => _shelfLife;
            set
            {
                if (value != null && value.Length > 4)
                    value = value[..4];

                if (SetProperty(ref _shelfLife, value ?? string.Empty))
                {
                    ShelfLifeError = string.Empty;
                    ValidateShelfLife();
                }
            }
        }

        private string _targetWeight = string.Empty;
        public string TargetWeight
        {
            get => _targetWeight;
            set
            {
                if (SetProperty(ref _targetWeight, value ?? string.Empty))
                {
                    TargetWeightError = string.Empty;
                    RecalculateWeightErrors();
                }
            }
        }

        private WeightUOM _targetWeightUOM = WeightUOM.Kg;
        public WeightUOM TargetWeightUOM
        {
            get => _targetWeightUOM;
            set
            {
                if (SetProperty(ref _targetWeightUOM, value))
                {
                    RecalculateWeightErrors();
                    RaisePropertyChanged(nameof(ScaleResolutionDisplay));
                    RaisePropertyChanged(nameof(MaxWeightDisplay));
                    RaisePropertyChanged(nameof(MinWeightHelperText));
                    RaisePropertyChanged(nameof(MaxWeightHelperText));
                    RaisePropertyChanged(nameof(TargetWeightMaxHelperText));
                }
            }
        }

        public List<string> WeightUOMOptions { get; } = new List<string> { "kg", "g" };

        public string SelectedWeightUOM
        {
            get => TargetWeightUOM == WeightUOM.Kg ? "kg" : "g";
            set
            {
                TargetWeightUOM = value == "kg" ? WeightUOM.Kg : WeightUOM.Gram;
                RaisePropertyChanged();
            }
        }

        private bool _checkWeigherRequired = true;
        public bool CheckWeigherRequired
        {
            get => _checkWeigherRequired;
            set
            {
                if (SetProperty(ref _checkWeigherRequired, value))
                {
                    // Clear errors when toggling
                    if (!value)
                    {
                        TargetWeightError = string.Empty;
                        TMinusPercentError = string.Empty;
                        TPlusPercentError = string.Empty;
                        MinWeightError = string.Empty;
                        MaxWeightError = string.Empty;
                    }
                    RaisePropertyChanged(nameof(IsWeightFieldsRequired));
                    RaisePropertyChanged(nameof(TargetWeightHelperText));
                    RaisePropertyChanged(nameof(TMinusPercentHelperText));
                    RaisePropertyChanged(nameof(TPlusPercentHelperText));
                    RecalculateWeightErrors();
                }
            }
        }

        public bool IsWeightFieldsRequired => CheckWeigherRequired;

        public string TargetWeightHelperText => CheckWeigherRequired 
            ? "Positive number (Required if Check Weigher is Yes)" 
            : "Positive number (Disabled - Check Weigher is No)";

        public string TMinusPercentHelperText => CheckWeigherRequired 
            ? "Positive number (Required if Check Weigher is Yes)" 
            : "Positive number (Disabled - Check Weigher is No)";

        public string TPlusPercentHelperText => CheckWeigherRequired 
            ? "Positive number (Required if Check Weigher is Yes)" 
            : "Positive number (Disabled - Check Weigher is No)";

        private string _tMinusPercent = string.Empty;
        public string TMinusPercent
        {
            get => _tMinusPercent;
            set
            {
                if (SetProperty(ref _tMinusPercent, value ?? string.Empty))
                {
                    TMinusPercentError = string.Empty;
                    RecalculateWeightErrors();
                }
            }
        }

        private string _tPlusPercent = string.Empty;
        public string TPlusPercent
        {
            get => _tPlusPercent;
            set
            {
                if (SetProperty(ref _tPlusPercent, value ?? string.Empty))
                {
                    TPlusPercentError = string.Empty;
                    RecalculateWeightErrors();
                }
            }
        }

        private string _toleranceMode = "Relative";
        public string ToleranceMode
        {
            get => _toleranceMode;
            set
            {
                if (SetProperty(ref _toleranceMode, value))
                {
                    IsToleranceModeRelative = value == "Relative";
                    IsToleranceModeAbsolute = value == "Absolute";
                    RecalculateWeightErrors();
                }
            }
        }

        private bool _isToleranceModeRelative = true;
        public bool IsToleranceModeRelative
        {
            get => _isToleranceModeRelative;
            set
            {
                if (SetProperty(ref _isToleranceModeRelative, value) && value)
                    ToleranceMode = "Relative";
            }
        }

        private bool _isToleranceModeAbsolute;
        public bool IsToleranceModeAbsolute
        {
            get => _isToleranceModeAbsolute;
            set
            {
                if (SetProperty(ref _isToleranceModeAbsolute, value) && value)
                    ToleranceMode = "Absolute";
            }
        }

        private string _minWeight = string.Empty;
        public string MinWeight
        {
            get => _minWeight;
            set
            {
                if (SetProperty(ref _minWeight, value ?? string.Empty))
                {
                    MinWeightError = string.Empty;
                    RecalculateWeightErrors();
                }
            }
        }

        private string _maxWeight = string.Empty;
        public string MaxWeight
        {
            get => _maxWeight;
            set
            {
                if (SetProperty(ref _maxWeight, value ?? string.Empty))
                {
                    MaxWeightError = string.Empty;
                    RecalculateWeightErrors();
                }
            }
        }

        private string _productNameError = string.Empty;
        public string ProductNameError
        {
            get => _productNameError;
            set => SetProperty(ref _productNameError, value);
        }

        private string _productCodeError = string.Empty;
        public string ProductCodeError
        {
            get => _productCodeError;
            set => SetProperty(ref _productCodeError, value);
        }

        private string _categoryError = string.Empty;
        public string CategoryError
        {
            get => _categoryError;
            set => SetProperty(ref _categoryError, value);
        }

        private string _variantError = string.Empty;
        public string VariantError
        {
            get => _variantError;
            set => SetProperty(ref _variantError, value);
        }

        private string _shelfLifeError = string.Empty;
        public string ShelfLifeError
        {
            get => _shelfLifeError;
            set => SetProperty(ref _shelfLifeError, value);
        }

        private string _targetWeightError = string.Empty;
        public string TargetWeightError
        {
            get => _targetWeightError;
            set => SetProperty(ref _targetWeightError, value);
        }

        private string _tMinusPercentError = string.Empty;
        public string TMinusPercentError
        {
            get => _tMinusPercentError;
            set => SetProperty(ref _tMinusPercentError, value);
        }

        private string _tPlusPercentError = string.Empty;
        public string TPlusPercentError
        {
            get => _tPlusPercentError;
            set => SetProperty(ref _tPlusPercentError, value);
        }

        private string _minWeightError = string.Empty;
        public string MinWeightError
        {
            get => _minWeightError;
            set => SetProperty(ref _minWeightError, value);
        }

        private string _maxWeightError = string.Empty;
        public string MaxWeightError
        {
            get => _maxWeightError;
            set => SetProperty(ref _maxWeightError, value);
        }

        // Scale-limit validation errors (App Settings Maximum Weight / Scale Resolution).
        // Kept separate from the required/range errors above so live recalculation never
        // wipes an error set during submit validation.
        private string _targetWeightLimitError = string.Empty;
        public string TargetWeightLimitError
        {
            get => _targetWeightLimitError;
            set => SetProperty(ref _targetWeightLimitError, value);
        }

        private string _minWeightLimitError = string.Empty;
        public string MinWeightLimitError
        {
            get => _minWeightLimitError;
            set => SetProperty(ref _minWeightLimitError, value);
        }

        private string _maxWeightLimitError = string.Empty;
        public string MaxWeightLimitError
        {
            get => _maxWeightLimitError;
            set => SetProperty(ref _maxWeightLimitError, value);
        }

        private bool _appSettingsLoaded;

        // Help text for the Absolute-mode Min/Max fields, generated from the
        // App Settings scale limits (Maximum Weight / Scale Resolution are stored
        // in kg) and converted to the material's selected UOM so the shown range
        // matches the units the user types. Purely informational.
        public string ScaleResolutionDisplay => FormatScaleLimit(_appSettings.ScaleResolution);
        public string MaxWeightDisplay => FormatScaleLimit(_appSettings.MaxWeight);

        public string MinWeightHelperText =>
            string.IsNullOrEmpty(ScaleResolutionDisplay)
                ? "Minimum acceptable weight"
                : $"Minimum weight must be at least {ScaleResolutionDisplay}";

        public string MaxWeightHelperText =>
            string.IsNullOrEmpty(MaxWeightDisplay)
                ? "Maximum acceptable weight"
                : $"Maximum weight must not exceed {MaxWeightDisplay}";

        // Helper text for the Target Weight field: shows the actual scale maximum
        // from the App Settings instead of a hardcoded example.
        public string TargetWeightMaxHelperText =>
            string.IsNullOrEmpty(MaxWeightDisplay)
                ? "Max 8 chars (e.g., 9999.999)"
                : $"Maximum value: {MaxWeightDisplay}";

        private string FormatScaleLimit(decimal kgValue)
        {
            if (kgValue <= 0)
                return string.Empty;
            if (TargetWeightUOM == WeightUOM.Kg)
                return $"{kgValue:0.###} kg";
            return $"{kgValue * 1000:0.0} g";
        }

        public ObservableCollection<Category> Categories => _categories;
        public ObservableCollection<Variant> Variants => _variants;

        public ICommand SubmitCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand BrowseImageCommand { get; }
        public ICommand RemoveImageCommand { get; }

        public MaterialsCreateViewModel(MaterialsViewModel parentViewModel, Material? editingMaterial,
            ObservableCollection<Category> categories, ObservableCollection<Variant> variants)
        {
            _parentViewModel = parentViewModel;
            _editingMaterial = editingMaterial;
            _categories = new ObservableCollection<Category>(categories);
            _variants = new ObservableCollection<Variant>(variants);
            _auditService = new AuditService();

            // Load app settings asynchronously (Maximum Weight / Scale Resolution are
            // stored in kg) so live scale-limit validation errors can be shown against
            // the configured scale limits. IMPORTANT: never block the UI thread here
            // with .GetAwaiter()/.Result/.Wait() - the file read is real async I/O and
            // its continuation is posted back to the UI thread, which deadlocks while
            // it is blocked.
            _ = LoadAppSettingsAsync();

            // Get current username from parent
            _currentUsername = parentViewModel.GetCurrentUsername();

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuiteFusion", "proweigh_suite_fusion.db");
            _materialService = new MaterialService(new MaterialRepository(dbPath));

            if (_editingMaterial != null)
            {
                ProductName = _editingMaterial.ProductName;
                ProductCode = _editingMaterial.ProductCode;
                SelectedCategory = _categories.FirstOrDefault(c => c.Id == _editingMaterial.CategoryId);
                SelectedVariant = _variants.FirstOrDefault(v => v.Id == _editingMaterial.VariantId);
                IsActive = _editingMaterial.IsActive;
                ShelfLife = _editingMaterial.ShelfLife.ToString();
                TargetWeight = _editingMaterial.TargetWeight > 0 ? _editingMaterial.TargetWeight.ToString() : string.Empty;
                TargetWeightUOM = _editingMaterial.TargetWeightUOM;
                CheckWeigherRequired = _editingMaterial.CheckWeigherRequired;
                TMinusPercent = _editingMaterial.TMinusPercent > 0 ? _editingMaterial.TMinusPercent.ToString() : string.Empty;
                TPlusPercent = _editingMaterial.TPlusPercent > 0 ? _editingMaterial.TPlusPercent.ToString() : string.Empty;
                ToleranceMode = _editingMaterial.ToleranceMode;
                MinWeight = _editingMaterial.MinWeight > 0 ? _editingMaterial.MinWeight.ToString() : string.Empty;
                MaxWeight = _editingMaterial.MaxWeight > 0 ? _editingMaterial.MaxWeight.ToString() : string.Empty;
                ImagePath = _editingMaterial.ImagePath;
            }
            else
            {
                // Set default for new material: CheckWeigherRequired = true
                CheckWeigherRequired = true;
            }

            SubmitCommand = new RelayCommand(async _ => await SubmitAsync());
            CancelCommand = new RelayCommand(async _ => await _parentViewModel.CancelCreate());
            BrowseImageCommand = new RelayCommand(_ => BrowseImage());
            RemoveImageCommand = new RelayCommand(_ => RemoveImage(), _ => HasImage);
        }

        private void BrowseImage()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Select Material Image",
                    Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp|All Files|*.*",
                    CheckFileExists = true
                };
                if (dlg.ShowDialog() != true) return;
                var sourcePath = dlg.FileName;

                // Open crop window (user can adjust crop, then confirm)
                var cropWindow = new ImageCropWindow(sourcePath)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                if (cropWindow.ShowDialog() != true || string.IsNullOrWhiteSpace(cropWindow.CroppedImagePath))
                    return;

                ImagePath = cropWindow.CroppedImagePath;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveImage()
        {
            ImagePath = null;
        }

        private void ValidateShelfLife()
        {
            if (string.IsNullOrWhiteSpace(ShelfLife) || !int.TryParse(ShelfLife, out int days))
            {
                ShelfLifeError = "Shelf Life must be a positive number";
            }
            else if (days <= 0)
            {
                ShelfLifeError = "Shelf Life must be a positive number";
            }
            else if (days > 9999)
            {
                ShelfLifeError = "Shelf Life cannot exceed 9999 days";
            }
            else
            {
                ShelfLifeError = string.Empty;
            }
        }

        private async Task LoadAppSettingsAsync()
        {
            try
            {
                _appSettings = await _appSettingsService.LoadAsync();
                _appSettingsLoaded = true;
                RecalculateWeightErrors();
                RaisePropertyChanged(nameof(ScaleResolutionDisplay));
                RaisePropertyChanged(nameof(MaxWeightDisplay));
                RaisePropertyChanged(nameof(MinWeightHelperText));
                RaisePropertyChanged(nameof(MaxWeightHelperText));
                RaisePropertyChanged(nameof(TargetWeightMaxHelperText));
            }
            catch
            {
                // Keep the default settings; limit validation simply stays inactive.
            }
        }

        /// <summary>
        /// Computes the scale-limit validation messages for the current input.
        /// Relative mode checks the calculated (Target +/- %) bounds; Absolute mode
        /// checks the entered Min/Max weights directly. Returns empty strings when
        /// the limits are satisfied (or the scale limits are not configured).
        /// </summary>
        private (string TargetError, string MinError, string MaxError) GetWeightLimitErrors()
        {
            var uom = TargetWeightUOM == WeightUOM.Kg ? "kg" : "g";
            double toKg = TargetWeightUOM == WeightUOM.Kg ? 1.0 : 0.001;
            double scaleResolutionKg = (double)_appSettings.ScaleResolution;
            double maxWeightKg = (double)_appSettings.MaxWeight;

            string targetError = string.Empty, minError = string.Empty, maxError = string.Empty;

            if (IsToleranceModeRelative)
            {
                if (double.TryParse(TargetWeight, out double tw) && tw > 0
                    && double.TryParse(TPlusPercent, out double tp)
                    && double.TryParse(TMinusPercent, out double tm))
                {
                    double maxVal = tw * (1 + tp / 100.0);
                    double minVal = tw * (1 - tm / 100.0);
                    var messages = new List<string>();

                    if (maxWeightKg > 0 && maxVal * toKg > maxWeightKg)
                        messages.Add($"Calculated maximum (Target + T+%) = {maxVal:0.###} {uom} exceeds the scale's Maximum Weight of {maxWeightKg / toKg:0.###} {uom}.");

                    if (scaleResolutionKg > 0 && minVal * toKg < scaleResolutionKg)
                        messages.Add($"Calculated minimum (Target - T-%) = {minVal:0.###} {uom} is below the scale's Scale Resolution of {scaleResolutionKg / toKg:0.###} {uom}.");

                    if (messages.Count > 0)
                        targetError = string.Join(" ", messages);
                }
            }
            else // Absolute
            {
                if (maxWeightKg > 0 && double.TryParse(MaxWeight, out double maxVal) && maxVal > 0
                    && maxVal * toKg > maxWeightKg)
                {
                    maxError = $"Max Weight {maxVal:0.###} {uom} exceeds the scale's Maximum Weight of {maxWeightKg / toKg:0.###} {uom}.";
                }

                if (scaleResolutionKg > 0 && double.TryParse(MinWeight, out double minVal) && minVal > 0
                    && minVal * toKg < scaleResolutionKg)
                {
                    minError = $"Min Weight {minVal:0.###} {uom} is below the scale's Scale Resolution of {scaleResolutionKg / toKg:0.###} {uom}.";
                }
            }

            return (targetError, minError, maxError);
        }

        // Recomputes the scale-limit validation errors after any weight-related input
        // changes, so the red validation messages update live while the user types.
        private void RecalculateWeightErrors()
        {
            TargetWeightLimitError = string.Empty;
            MinWeightLimitError = string.Empty;
            MaxWeightLimitError = string.Empty;

            if (!CheckWeigherRequired)
                return;

            var (targetError, minError, maxError) = GetWeightLimitErrors();
            TargetWeightLimitError = targetError;
            MinWeightLimitError = minError;
            MaxWeightLimitError = maxError;
        }

        private async Task SubmitAsync()
        {
            // Ensure scale limits are loaded before validating (settings load is async).
            if (!_appSettingsLoaded)
                await LoadAppSettingsAsync();

            if (!await ValidateFormAsync())
                return;

            try
            {
                if (_editingMaterial == null)
                {
                    var material = new Material
                    {
                        ProductName = ProductName.Trim(),
                        ProductCode = ProductCode.Trim(),
                        CategoryId = SelectedCategory!.Id,
                        VariantId = SelectedVariant!.Id,
                        IsActive = IsActive,
                        ShelfLife = int.TryParse(ShelfLife, out int sl) ? sl : 365,
                        TargetWeight = double.TryParse(TargetWeight, out double tw) ? tw : 0,
                        TargetWeightUOM = TargetWeightUOM,
                        CheckWeigherRequired = CheckWeigherRequired,
                        TMinusPercent = double.TryParse(TMinusPercent, out double tmv) ? tmv : 0,
                        TPlusPercent = double.TryParse(TPlusPercent, out double tpv) ? tpv : 0,
                        ToleranceMode = ToleranceMode,
                        MinWeight = double.TryParse(MinWeight, out double minw) ? minw : 0,
                        MaxWeight = double.TryParse(MaxWeight, out double maxw) ? maxw : 0,
                        ImagePath = string.IsNullOrWhiteSpace(ImagePath) ? null : ImagePath!.Trim(),
                        CreatedDate = DateTime.Now
                    };

                    await _materialService.CreateAsync(material);
                    
                    // Log material creation
                    await _auditService.LogMaterialCreatedAsync(_currentUsername, material);
                    
                    MessageBox.Show("Product created successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Store old values for audit
                    var oldMaterial = new Material
                    {
                        Id = _editingMaterial.Id,
                        ProductName = _editingMaterial.ProductName,
                        ProductCode = _editingMaterial.ProductCode,
                        CategoryId = _editingMaterial.CategoryId,
                        VariantId = _editingMaterial.VariantId,
                        IsActive = _editingMaterial.IsActive,
                        TargetWeight = _editingMaterial.TargetWeight,
                        CheckWeigherRequired = _editingMaterial.CheckWeigherRequired
                    };
                    
                    _editingMaterial.ProductName = ProductName.Trim();
                    _editingMaterial.ProductCode = ProductCode.Trim();
                    _editingMaterial.CategoryId = SelectedCategory!.Id;
                    _editingMaterial.VariantId = SelectedVariant!.Id;
                    _editingMaterial.IsActive = IsActive;
                    _editingMaterial.ShelfLife = int.TryParse(ShelfLife, out int sl2) ? sl2 : 365;
                    _editingMaterial.TargetWeight = double.TryParse(TargetWeight, out double tw2) ? tw2 : 0;
                    _editingMaterial.TargetWeightUOM = TargetWeightUOM;
                    _editingMaterial.CheckWeigherRequired = CheckWeigherRequired;
                    _editingMaterial.TMinusPercent = double.TryParse(TMinusPercent, out double tmu) ? tmu : 0;
                    _editingMaterial.TPlusPercent = double.TryParse(TPlusPercent, out double tpu) ? tpu : 0;
                    _editingMaterial.ToleranceMode = ToleranceMode;
                    _editingMaterial.MinWeight = double.TryParse(MinWeight, out double minu) ? minu : 0;
                    _editingMaterial.MaxWeight = double.TryParse(MaxWeight, out double maxu) ? maxu : 0;
                    _editingMaterial.ImagePath = string.IsNullOrWhiteSpace(ImagePath) ? null : ImagePath!.Trim();
                    _editingMaterial.ModifiedDate = DateTime.Now;

                    await _materialService.UpdateAsync(_editingMaterial);
                    
                    // Log material update
                    await _auditService.LogMaterialUpdatedAsync(_currentUsername, oldMaterial, _editingMaterial);
                    
                    MessageBox.Show("Product updated successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                await _parentViewModel.CancelCreate();
            }
            catch (Exception ex)
            {
                // Log error
                await _auditService.LogErrorAsync(_currentUsername, "Save material", ex);
                
                MessageBox.Show($"Error saving product: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<bool> ValidateFormAsync()
        {
            ProductNameError = string.Empty;
            ProductCodeError = string.Empty;
            CategoryError = string.Empty;
            VariantError = string.Empty;
            ShelfLifeError = string.Empty;
            TargetWeightError = string.Empty;
            TMinusPercentError = string.Empty;
            TPlusPercentError = string.Empty;
            MinWeightError = string.Empty;
            MaxWeightError = string.Empty;
            TargetWeightLimitError = string.Empty;
            MinWeightLimitError = string.Empty;
            MaxWeightLimitError = string.Empty;

            bool hasError = false;

            var materialName = ProductName.Trim();
            if (string.IsNullOrWhiteSpace(materialName))
            {
                ProductNameError = "Material Name cannot be empty";
                hasError = true;
            }
            else if (materialName.Length < 2)
            {
                ProductNameError = "Material Name must be at least 2 characters";
                hasError = true;
            }
            else if (materialName.Length > 100)
            {
                ProductNameError = "Material Name cannot exceed 100 characters";
                hasError = true;
            }

            // Validate Product Code
            if (string.IsNullOrWhiteSpace(ProductCode))
            {
                ProductCodeError = "Material Code is required";
                hasError = true;
            }
            else if (ProductCode.Trim().Length < 1)
            {
                ProductCodeError = "Material Code cannot be empty";
                hasError = true;
            }
            else if (ProductCode.Length > 50)
            {
                ProductCodeError = "Material Code cannot exceed 50 characters";
                hasError = true;
            }
            else
            {
                var repo = new MaterialRepository(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ProWeighSuiteFusion", "proweigh_suite_fusion.db"));

                int? excludeId = _editingMaterial?.Id;
                bool codeExists = await repo.ProductCodeExistsAsync(ProductCode.Trim(), excludeId);

                if (codeExists)
                {
                    ProductCodeError = "Material Code already exists. Please use a unique code.";
                    hasError = true;
                }
            }

            if (SelectedCategory == null)
            {
                CategoryError = "Category is required";
                hasError = true;
            }

            if (SelectedVariant == null)
            {
                VariantError = "Variant is required";
                hasError = true;
            }

            if (string.IsNullOrWhiteSpace(ShelfLife) || !int.TryParse(ShelfLife, out int slDays) || slDays <= 0)
            {
                ShelfLifeError = "Shelf Life must be a positive number";
                hasError = true;
            }
            else if (slDays > 9999)
            {
                ShelfLifeError = "Shelf Life cannot exceed 9999 days";
                hasError = true;
            }

            if (CheckWeigherRequired)
            {
                if (IsToleranceModeRelative)
                {
                    if (string.IsNullOrWhiteSpace(TargetWeight) || !double.TryParse(TargetWeight, out double twVal) || twVal <= 0)
                    {
                        TargetWeightError = "Target Weight is required when Check Weigher is enabled";
                        hasError = true;
                    }
                    else if (twVal > 9999.999)
                    {
                        TargetWeightError = "Target Weight cannot exceed 9999.999 (8 chars max)";
                        hasError = true;
                    }

                    if (!string.IsNullOrWhiteSpace(TMinusPercent) && double.TryParse(TMinusPercent, out double tmVal))
                    {
                        if (tmVal < 0)
                        {
                            TMinusPercentError = "T- % must be a positive number";
                            hasError = true;
                        }
                        else if (tmVal == 0)
                        {
                            TMinusPercentError = "T- % is required when Check Weigher is enabled (Relative mode)";
                            hasError = true;
                        }
                        else if (tmVal > 100)
                        {
                            TMinusPercentError = "T- % cannot exceed 100%";
                            hasError = true;
                        }
                    }
                    else if (CheckWeigherRequired && IsToleranceModeRelative)
                    {
                        TMinusPercentError = "T- % is required when Check Weigher is enabled (Relative mode)";
                        hasError = true;
                    }

                    if (!string.IsNullOrWhiteSpace(TPlusPercent) && double.TryParse(TPlusPercent, out double tpVal))
                    {
                        if (tpVal < 0)
                        {
                            TPlusPercentError = "T+ % must be a positive number";
                            hasError = true;
                        }
                        else if (tpVal == 0)
                        {
                            TPlusPercentError = "T+ % is required when Check Weigher is enabled (Relative mode)";
                            hasError = true;
                        }
                        else if (tpVal > 100)
                        {
                            TPlusPercentError = "T+ % cannot exceed 100%";
                            hasError = true;
                        }
                    }
                    else if (CheckWeigherRequired && IsToleranceModeRelative)
                    {
                        TPlusPercentError = "T+ % is required when Check Weigher is enabled (Relative mode)";
                        hasError = true;
                    }

                    // Scale-limit validation: Target +/- % bounds must stay within the
                    // App Settings Maximum Weight and Scale Resolution.
                    var (targetLimitError, _, _) = GetWeightLimitErrors();
                    if (!string.IsNullOrWhiteSpace(targetLimitError))
                    {
                        TargetWeightLimitError = targetLimitError;
                        hasError = true;
                    }
                }
                else // Absolute
                {
                    if (string.IsNullOrWhiteSpace(MinWeight) || !double.TryParse(MinWeight, out double minVal) || minVal <= 0)
                    {
                        MinWeightError = "Min Weight is required when Check Weigher is enabled (Absolute mode)";
                        hasError = true;
                    }
                    else if (minVal > 9999.999)
                    {
                        MinWeightError = "Min Weight cannot exceed 9999.999";
                        hasError = true;
                    }

                    if (string.IsNullOrWhiteSpace(MaxWeight) || !double.TryParse(MaxWeight, out double maxVal) || maxVal <= 0)
                    {
                        MaxWeightError = "Max Weight is required when Check Weigher is enabled (Absolute mode)";
                        hasError = true;
                    }
                    else if (maxVal > 9999.999)
                    {
                        MaxWeightError = "Max Weight cannot exceed 9999.999";
                        hasError = true;
                    }

                    if (!hasError && double.TryParse(MinWeight, out minVal) && double.TryParse(MaxWeight, out maxVal) && minVal > 0 && maxVal > 0 && minVal >= maxVal)
                    {
                        MaxWeightError = "Max Weight must be greater than Min Weight";
                        hasError = true;
                    }

                    // Scale-limit validation: Min/Max weights must stay within the
                    // App Settings Maximum Weight and Scale Resolution.
                    var (_, minLimitError, maxLimitError) = GetWeightLimitErrors();
                    if (!string.IsNullOrWhiteSpace(minLimitError))
                    {
                        MinWeightLimitError = minLimitError;
                        hasError = true;
                    }
                    if (!string.IsNullOrWhiteSpace(maxLimitError))
                    {
                        MaxWeightLimitError = maxLimitError;
                        hasError = true;
                    }
                }
            }

            return !hasError;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class MaterialCsvImportResult
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; } = new();
        public List<MaterialCsvImportDuplicate> Duplicates { get; } = new();
    }

    public class MaterialCsvImportDuplicate
    {
        public int RowNumber { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
    }

    public class MaterialService
    {
        private readonly IMaterialRepository _repo;

        public MaterialService(IMaterialRepository repo)
        {
            _repo = repo;
        }

        public Task InitializeAsync() => _repo.InitializeAsync();
        public Task<IEnumerable<Material>> GetAllAsync() => _repo.GetAllAsync();
        public Task<Material?> GetAsync(int id) => _repo.GetAsync(id);
        public Task<Material?> GetByCodeAsync(string productCode) => _repo.GetByCodeAsync(productCode);
        public Task<int> CreateAsync(Material material) => _repo.CreateAsync(material);
        public Task<int> UpdateAsync(Material material) => _repo.UpdateAsync(material);
        public Task<int> DeleteAsync(int id) => _repo.DeleteAsync(id);
        public Task<bool> ProductCodeExistsAsync(string productCode, int? excludeId = null) => _repo.ProductCodeExistsAsync(productCode, excludeId);
        public Task<string> GenerateNextProductCodeAsync() => _repo.GenerateNextProductCodeAsync();

        public static string GetCsvTemplate()
        {
            return "ProductName,ProductCode,CategoryName,VariantName,TargetWeight,TargetWeightUOM,TMinusPercent,TPlusPercent,ShelfLife,CheckWeigherRequired,IsActive,MinWeight,MaxWeight,ToleranceMode\r\n" +
                   "Sample Product,MAT00001,Electronics,Whole,1.500,Kg,2,2,365,No,Active,1.400,1.600,Relative\r\n";
        }

        public async Task<(List<MaterialCsvImportDuplicate> duplicates, List<(int row, Material material, string categoryName, string variantName)> allRows, List<string> errors)>
            ParseCsvAsync(string filePath, List<Category> categories, List<Variant> variants)
        {
            var duplicates = new List<MaterialCsvImportDuplicate>();
            var allRows = new List<(int row, Material material, string categoryName, string variantName)>();
            var errors = new List<string>();

            var lines = await File.ReadAllLinesAsync(filePath);
            if (lines.Length < 2)
            {
                errors.Add("CSV file is empty or has no data rows.");
                return (duplicates, allRows, errors);
            }

            var header = lines[0].Split(',');
            if (header.Length < 2 ||
                !header[0].Trim().Equals("ProductName", StringComparison.OrdinalIgnoreCase) ||
                !header[1].Trim().Equals("ProductCode", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Invalid CSV format. Expected header: ProductName,ProductCode,CategoryName,VariantName,TargetWeight,TargetWeightUOM,TMinusPercent,TPlusPercent,ShelfLife,CheckWeigherRequired,IsActive,MinWeight,MaxWeight,ToleranceMode");
                return (duplicates, allRows, errors);
            }

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = ParseCsvLine(line);

                var productName = cols.Length > 0 ? cols[0].Trim() : string.Empty;
                var productCode = cols.Length > 1 ? cols[1].Trim() : string.Empty;

                if (string.IsNullOrWhiteSpace(productName))
                {
                    errors.Add($"Row {i + 1}: ProductName is required.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    errors.Add($"Row {i + 1}: ProductCode is required.");
                    continue;
                }

                var categoryName = cols.Length > 2 ? cols[2].Trim() : string.Empty;
                var variantName = cols.Length > 3 ? cols[3].Trim() : string.Empty;

                // Resolve category
                var category = categories.FirstOrDefault(c =>
                    c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
                if (category == null)
                {
                    errors.Add($"Row {i + 1}: Category '{categoryName}' not found. Please create it first.");
                    continue;
                }

                // Resolve variant
                var variant = variants.FirstOrDefault(v =>
                    v.Name.Equals(variantName, StringComparison.OrdinalIgnoreCase));
                if (variant == null)
                {
                    errors.Add($"Row {i + 1}: Variant '{variantName}' not found. Please create it first.");
                    continue;
                }

                if (!double.TryParse(cols.Length > 4 ? cols[4].Trim() : "0", out var targetWeight))
                    targetWeight = 0;

                var uomText = cols.Length > 5 ? cols[5].Trim() : "Kg";
                var uom = uomText.Equals("Gram", StringComparison.OrdinalIgnoreCase) ||
                          uomText.Equals("g", StringComparison.OrdinalIgnoreCase)
                    ? WeightUOM.Gram : WeightUOM.Kg;

                if (!double.TryParse(cols.Length > 6 ? cols[6].Trim() : "0", out var tMinus))
                    tMinus = 0;
                if (!double.TryParse(cols.Length > 7 ? cols[7].Trim() : "0", out var tPlus))
                    tPlus = 0;
                if (!int.TryParse(cols.Length > 8 ? cols[8].Trim() : "365", out var shelfLife))
                    shelfLife = 365;

                var checkWeigherText = cols.Length > 9 ? cols[9].Trim() : "No";
                var checkWeigher = checkWeigherText.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                                   checkWeigherText.Equals("True", StringComparison.OrdinalIgnoreCase);

                var isActiveText = cols.Length > 10 ? cols[10].Trim() : "Active";
                var isActive = !isActiveText.Equals("Inactive", StringComparison.OrdinalIgnoreCase);

                if (!double.TryParse(cols.Length > 11 ? cols[11].Trim() : "0", out var minWeight))
                    minWeight = 0;
                if (!double.TryParse(cols.Length > 12 ? cols[12].Trim() : "0", out var maxWeight))
                    maxWeight = 0;

                var toleranceMode = cols.Length > 13 ? cols[13].Trim() : "Relative";
                if (!toleranceMode.Equals("Absolute", StringComparison.OrdinalIgnoreCase))
                    toleranceMode = "Relative";

                var material = new Material
                {
                    ProductName = productName,
                    ProductCode = productCode,
                    CategoryId = category.Id,
                    CategoryName = category.Name,
                    VariantId = variant.Id,
                    VariantName = variant.Name,
                    TargetWeight = targetWeight,
                    TargetWeightUOM = uom,
                    TMinusPercent = tMinus,
                    TPlusPercent = tPlus,
                    ShelfLife = shelfLife,
                    CheckWeigherRequired = checkWeigher,
                    IsActive = isActive,
                    MinWeight = minWeight,
                    MaxWeight = maxWeight,
                    ToleranceMode = toleranceMode,
                    CreatedDate = DateTime.Now
                };

                allRows.Add((i + 1, material, categoryName, variantName));

                if (await _repo.ProductCodeExistsAsync(productCode))
                {
                    duplicates.Add(new MaterialCsvImportDuplicate
                    {
                        RowNumber = i + 1,
                        ProductCode = productCode,
                        ProductName = productName
                    });
                }
            }

            return (duplicates, allRows, errors);
        }

        public async Task<MaterialCsvImportResult> ImportParsedAsync(
            List<(int row, Material material, string categoryName, string variantName)> allRows,
            HashSet<string> overrideCodes)
        {
            var result = new MaterialCsvImportResult();

            foreach (var (_, material, _, _) in allRows)
            {
                var existing = await _repo.GetByCodeAsync(material.ProductCode);

                if (existing != null)
                {
                    if (overrideCodes.Contains(material.ProductCode, StringComparer.OrdinalIgnoreCase))
                    {
                        material.Id = existing.Id;
                        material.CreatedDate = existing.CreatedDate;
                        material.ModifiedDate = DateTime.Now;
                        await _repo.UpdateAsync(material);
                        result.Updated++;
                    }
                    else
                    {
                        result.Skipped++;
                    }
                }
                else
                {
                    await _repo.CreateAsync(material);
                    result.Inserted++;
                }
            }

            return result;
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
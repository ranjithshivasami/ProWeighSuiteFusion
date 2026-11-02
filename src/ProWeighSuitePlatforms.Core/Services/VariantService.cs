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
    public class VariantCsvImportResult
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; } = new();
        public List<VariantCsvImportDuplicate> Duplicates { get; } = new();
    }

    public class VariantCsvImportDuplicate
    {
        public int RowNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Active { get; set; }
    }

    public class VariantService
    {
        private readonly VariantRepository _repository;

        public VariantService(VariantRepository repository)
        {
            _repository = repository;
        }

        public async Task InitializeAsync()
        {
            await _repository.InitializeAsync();
        }

        public async Task<List<Variant>> GetAllAsync()
        {
            return await _repository.GetAllAsync();
        }

        public async Task<Variant?> GetByIdAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<int> AddAsync(Variant variant)
        {
            return await _repository.AddAsync(variant);
        }

        public async Task UpdateAsync(Variant variant)
        {
            await _repository.UpdateAsync(variant);
        }

        public async Task DeleteAsync(int id)
        {
            await _repository.DeleteAsync(id);
        }

        public async Task<bool> NameExistsAsync(string name, int? excludeId = null)
        {
            return await _repository.NameExistsAsync(name, excludeId);
        }

        public static string GetCsvTemplate()
        {
            return "Name,Description,Active\r\n" +
                   "SampleVariant1,Example description for SampleVariant1,Active\r\n" +
                   "SampleVariant2,Example description for SampleVariant2,Inactive\r\n";
        }

        public async Task<(List<VariantCsvImportDuplicate> duplicates, List<(int row, Variant variant)> allRows, List<string> errors)>
            ParseCsvAsync(string filePath)
        {
            var duplicates = new List<VariantCsvImportDuplicate>();
            var allRows = new List<(int row, Variant variant)>();
            var errors = new List<string>();

            var lines = await File.ReadAllLinesAsync(filePath);
            if (lines.Length < 2)
            {
                errors.Add("CSV file is empty or has no data rows.");
                return (duplicates, allRows, errors);
            }

            var header = lines[0].Split(',');
            if (header.Length < 1 || !header[0].Trim().Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Invalid CSV format. Expected header: Name,Description,Active");
                return (duplicates, allRows, errors);
            }

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = ParseCsvLine(line);

                if (cols.Length < 1 || string.IsNullOrWhiteSpace(cols[0]))
                {
                    errors.Add($"Row {i + 1}: Name is required.");
                    continue;
                }

                var name = cols[0].Trim();
                if (name.Length < 2 || name.Length > 100)
                {
                    errors.Add($"Row {i + 1}: Name '{name}' must be between 2 and 100 characters.");
                    continue;
                }

                var description = cols.Length > 1 ? cols[1].Trim() : string.Empty;
                var activeText = cols.Length > 2 ? cols[2].Trim() : "Active";
                var active = !activeText.Equals("Inactive", StringComparison.OrdinalIgnoreCase);

                var variant = new Variant
                {
                    Name = name,
                    Description = description,
                    Active = active,
                    CreatedAt = DateTime.Now
                };

                allRows.Add((i + 1, variant));

                if (await _repository.NameExistsAsync(name))
                {
                    duplicates.Add(new VariantCsvImportDuplicate
                    {
                        RowNumber = i + 1,
                        Name = name,
                        Description = description,
                        Active = active
                    });
                }
            }

            return (duplicates, allRows, errors);
        }

        public async Task<VariantCsvImportResult> ImportParsedAsync(
            List<(int row, Variant variant)> allRows,
            HashSet<string> overrideNames)
        {
            var result = new VariantCsvImportResult();
            var existing = await _repository.GetAllAsync();

            foreach (var (_, variant) in allRows)
            {
                var match = existing.FirstOrDefault(v =>
                    v.Name.Equals(variant.Name, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    if (overrideNames.Contains(variant.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        await _repository.UpdateAsync(new Variant
                        {
                            Id = match.Id,
                            Name = variant.Name,
                            Description = variant.Description,
                            Active = variant.Active,
                            CreatedAt = match.CreatedAt
                        });
                        result.Updated++;
                    }
                    else
                    {
                        result.Skipped++;
                    }
                }
                else
                {
                    await _repository.AddAsync(variant);
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

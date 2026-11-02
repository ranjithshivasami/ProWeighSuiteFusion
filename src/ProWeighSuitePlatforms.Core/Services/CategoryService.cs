using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class CsvImportResult
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; } = new();
        public List<CsvImportDuplicate> Duplicates { get; } = new();
    }

    public class CsvImportDuplicate
    {
        public int RowNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Status { get; set; }
    }

    public class CategoryService
    {
        private readonly CategoryRepository _repository;

        public CategoryService(CategoryRepository repository)
        {
            _repository = repository;
        }

        public async Task InitializeAsync()
        {
            await _repository.InitializeAsync();
        }

        public async Task<List<Category>> GetAllAsync()
        {
            return await _repository.GetAllAsync();
        }

        public async Task<Category?> GetByIdAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<int> AddAsync(Category category)
        {
            return await _repository.AddAsync(category);
        }

        public async Task UpdateAsync(Category category)
        {
            await _repository.UpdateAsync(category);
        }

        public async Task DeleteAsync(int id)
        {
            await _repository.DeleteAsync(id);
        }

        public async Task<bool> NameExistsAsync(string name, int? excludeId = null)
        {
            return await _repository.NameExistsAsync(name, excludeId);
        }

        /// <summary>
        /// Generates a CSV template string for category import with sample data.
        /// </summary>
        public static string GetCsvTemplate()
        {
            return "Name,Description,Status\r\n" +
                   "SampleCategory1,Example description for SampleCategory1,Active\r\n" +
                   "SampleCategory2,Example description for SampleCategory2,Inactive\r\n";
        }

        /// <summary>
        /// Parses a CSV file and returns duplicates found (rows whose Name already exists in DB).
        /// The caller decides whether to skip or override duplicates.
        /// </summary>
        public async Task<(List<CsvImportDuplicate> duplicates, List<(int row, Category cat)> allRows, List<string> errors)>
            ParseCsvAsync(string filePath)
        {
            var duplicates = new List<CsvImportDuplicate>();
            var allRows = new List<(int row, Category cat)>();
            var errors = new List<string>();

            var lines = await File.ReadAllLinesAsync(filePath);
            if (lines.Length < 2)
            {
                errors.Add("CSV file is empty or has no data rows.");
                return (duplicates, allRows, errors);
            }

            // Validate header
            var header = lines[0].Split(',');
            if (header.Length < 1 || !header[0].Trim().Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Invalid CSV format. Expected header: Name,Description,Status");
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
                var statusText = cols.Length > 2 ? cols[2].Trim() : "Active";
                var status = !statusText.Equals("Inactive", StringComparison.OrdinalIgnoreCase);

                var cat = new Category
                {
                    Name = name,
                    Description = description,
                    Status = status,
                    CreatedAt = DateTime.Now
                };

                allRows.Add((i + 1, cat));

                if (await _repository.NameExistsAsync(name))
                {
                    duplicates.Add(new CsvImportDuplicate
                    {
                        RowNumber = i + 1,
                        Name = name,
                        Description = description,
                        Status = status
                    });
                }
            }

            return (duplicates, allRows, errors);
        }

        /// <summary>
        /// Imports categories from the parsed rows.
        /// overrideNames: set of names to override (update), others with duplicate names are skipped.
        /// </summary>
        public async Task<CsvImportResult> ImportParsedAsync(
            List<(int row, Category cat)> allRows,
            HashSet<string> overrideNames)
        {
            var result = new CsvImportResult();
            var existingCategories = await _repository.GetAllAsync();

            foreach (var (row, cat) in allRows)
            {
                var existing = existingCategories.FirstOrDefault(c =>
                    c.Name.Equals(cat.Name, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    if (overrideNames.Contains(cat.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        // Update existing record
                        var updated = new Category
                        {
                            Id = existing.Id,
                            Name = cat.Name,
                            Description = cat.Description,
                            Status = cat.Status,
                            CreatedAt = existing.CreatedAt
                        };
                        await _repository.UpdateAsync(updated);
                        result.Updated++;
                    }
                    else
                    {
                        result.Skipped++;
                    }
                }
                else
                {
                    await _repository.AddAsync(cat);
                    result.Inserted++;
                }
            }

            return result;
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

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

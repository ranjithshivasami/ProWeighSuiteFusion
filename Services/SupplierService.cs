using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class SupplierImportResult
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
    }

    public class SupplierService
    {
        private readonly SupplierRepository _repository;

        public SupplierService(SupplierRepository repository)
        {
            _repository = repository;
        }

        public Task InitializeAsync() => _repository.InitializeAsync();
        public Task<List<Supplier>> GetAllAsync() => _repository.GetAllAsync();
        public Task<Supplier?> GetByIdAsync(int id) => _repository.GetByIdAsync(id);
        public Task<int> AddAsync(Supplier s) => _repository.AddAsync(s);
        public Task UpdateAsync(Supplier s) => _repository.UpdateAsync(s);
        public Task DeleteAsync(int id) => _repository.DeleteAsync(id);
        public Task<bool> CodeExistsAsync(string code, int? excludeId = null) => _repository.CodeExistsAsync(code, excludeId);
        public Task<string> GenerateNextCodeAsync() => _repository.GenerateNextCodeAsync();

        public static string GetCsvTemplate()
        {
            return "SupplierCode,SupplierName,DisplayName,SupplierType,BusinessCategory,Status,LegalEntityType,RegistrationNumber,TaxId,Website,ContactPerson,Designation,Phone,Mobile,Email\r\n" +
                   "SUP-0001,Acme Raw Materials,Acme,Manufacturer,Raw Material,Active,Pvt Ltd,CIN12345,GSTIN12345,,John Smith,Manager,+911234567890,,john@acme.com\r\n";
        }

        public async Task<(List<(int row, Supplier s)> rows, List<string> errors)> ParseCsvAsync(string filePath)
        {
            var rows = new List<(int, Supplier)>();
            var errors = new List<string>();
            var lines = await File.ReadAllLinesAsync(filePath);
            if (lines.Length < 2)
            {
                errors.Add("CSV file is empty or has no data rows.");
                return (rows, errors);
            }
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var c = ParseCsvLine(line);
                string At(int idx) => c.Length > idx ? c[idx].Trim() : string.Empty;
                var s = new Supplier
                {
                    SupplierCode = At(0),
                    SupplierName = At(1),
                    DisplayName = At(2),
                    SupplierType = At(3),
                    BusinessCategory = At(4),
                    Status = !At(5).Equals("Inactive", StringComparison.OrdinalIgnoreCase),
                    LegalEntityType = At(6),
                    RegistrationNumber = At(7),
                    TaxId = At(8),
                    Website = At(9),
                    ContactPerson = At(10),
                    Designation = At(11),
                    Phone = At(12),
                    Mobile = At(13),
                    Email = At(14),
                    CreatedAt = DateTime.Now
                };
                // Only Supplier Code and Supplier Name are required; all other fields are optional.
                if (string.IsNullOrWhiteSpace(s.SupplierCode)) { errors.Add($"Row {i + 1}: Supplier Code is required."); continue; }
                if (string.IsNullOrWhiteSpace(s.SupplierName)) { errors.Add($"Row {i + 1}: Supplier Name is required."); continue; }
                rows.Add((i + 1, s));
            }
            return (rows, errors);
        }

        public async Task<SupplierImportResult> ImportParsedAsync(List<(int row, Supplier s)> rows, bool overwriteDuplicates)
        {
            var result = new SupplierImportResult();
            var existing = await _repository.GetAllAsync();
            var map = existing.ToDictionary(x => x.SupplierCode.Trim().ToLowerInvariant(), x => x);
            foreach (var (_, s) in rows)
            {
                var key = s.SupplierCode.Trim().ToLowerInvariant();
                if (map.TryGetValue(key, out var ex))
                {
                    if (overwriteDuplicates)
                    {
                        s.Id = ex.Id;
                        s.CreatedAt = ex.CreatedAt;
                        await _repository.UpdateAsync(s);
                        result.Updated++;
                    }
                    else result.Skipped++;
                }
                else
                {
                    await _repository.AddAsync(s);
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
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}

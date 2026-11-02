using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class ZplTemplateService
    {
        private readonly ZplTemplateRepository _repository;

        public ZplTemplateService()
        {
            var dbPath = AppConfigurationManager.GetDatabasePath();
            _repository = new ZplTemplateRepository(dbPath);
        }

        public ZplTemplateService(ZplTemplateRepository repository)
        {
            _repository = repository;
        }

        public async Task InitializeAsync()
        {
            await _repository.InitializeAsync();
        }

        public Task<List<ZplLabelTemplate>> GetAllAsync() => _repository.GetAllAsync();
        public Task<ZplLabelTemplate?> GetByIdAsync(int id) => _repository.GetByIdAsync(id);
        public Task<ZplLabelTemplate?> GetActiveAsync() => _repository.GetActiveAsync();
        public Task<int> CreateAsync(ZplLabelTemplate template) => _repository.CreateAsync(template);
        public Task<int> UpdateAsync(ZplLabelTemplate template) => _repository.UpdateAsync(template);
        public Task<int> DeleteAsync(int id) => _repository.DeleteAsync(id);
        public Task SetActiveAsync(int id) => _repository.SetActiveAsync(id);
        public Task<bool> NameExistsAsync(string name, int? excludeId = null) => _repository.NameExistsAsync(name, excludeId);

        /// <summary>
        /// Returns the list of supported placeholder tokens and their descriptions.
        /// </summary>
        public static List<(string Token, string Description)> GetAvailablePlaceholders()
        {
            return new List<(string, string)>
            {
                ("{{PrintTitle}}", "Print or Reprint title (resolves to PRINT or REPRINT)"),
                ("{{TransactionUUID}}", "Transaction UUID / ID"),
                ("{{BatchNumber}}", "Batch number"),
                ("{{MaterialCode}}", "Material / product code"),
                ("{{MaterialName}}", "Material / product name"),
                ("{{ActualWeight}}", "Actual weight (3 decimals, e.g. 10.052)"),
                ("{{ActualWeightWithUnit}}", "Actual weight with unit (e.g. 10.052 kg)"),
                ("{{TargetWeight}}", "Target weight (3 decimals)"),
                ("{{TargetWeightInt}}", "Target weight rounded (e.g. 10)"),
                ("{{Deviation}}", "Weight deviation (+/- 3 decimals)"),
                ("{{Status}}", "Accept / UnderWeight / OverWeight"),
                ("{{WeighedBy}}", "Login name of operator"),
                ("{{WeighedDate}}", "Weighed date (dd-MM-yyyy)"),
                ("{{WeighedTime}}", "Weighed time (HH.mm)"),
                ("{{WeighedDateTime}}", "Weighed date and time (dd-MM-yyyy HH.mm)"),
                ("{{Sequence}}", "Sequence number in batch"),
                ("{{CustomerName}}", "Customer name"),
                ("{{Location}}", "Scale location"),
                ("{{EquipmentId}}", "Equipment / scale ID"),
                ("{{TareWeight}}", "Tare weight (3 decimals)"),
                ("{{Remarks}}", "Remarks / notes"),
                ("{{PrintedDate}}", "Printed date (dd-MM-yyyy)"),
                ("{{PrintedTime}}", "Printed time (HH.mm)"),
                ("{{PrintedDateTime}}", "Printed date and time (dd-MM-yyyy HH:mm:ss)"),
                ("{{LowerWeight}}", "Lower tolerance weight"),
                ("{{UpperWeight}}", "Upper tolerance weight"),
                ("{{ManufacturingDate}}", "Manufacturing date"),
                ("{{ExpiryDate}}", "Expiry date"),
            };
        }

        /// <summary>
        /// Resolves all {{Placeholder}} tokens in the ZPL code using transaction data.
        /// </summary>
        public static string ResolveZpl(string zplTemplate, WeighingTransaction transaction, bool isReprint = false)
        {
            if (string.IsNullOrEmpty(zplTemplate))
                return string.Empty;

            var now = DateTime.Now;

            // Parse weighed date
            DateTime weighedDt = transaction.WeighedDateValue != default
                ? transaction.WeighedDateValue
                : (DateTime.TryParse(transaction.WeighedDate, out var parsed) ? parsed : DateTime.MinValue);

            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "{{PrintTitle}}", isReprint ? "REPRINT" : "PRINT" },
                { "{{TransactionUUID}}", transaction.Id.ToString() },
                { "{{BatchNumber}}", Escape(transaction.BatchNumber) },
                { "{{MaterialCode}}", Escape(transaction.MaterialCode) },
                { "{{MaterialName}}", Escape(transaction.MaterialName) },
                { "{{ActualWeight}}", transaction.ActualWeight.ToString("F3") },
                { "{{ActualWeightWithUnit}}", transaction.ActualWeight.ToString("F3") + " kg" },
                { "{{TargetWeight}}", transaction.TargetWeight.ToString("F3") },
                { "{{TargetWeightInt}}", transaction.TargetWeight.ToString("F0") },
                { "{{Deviation}}", transaction.Deviation.ToString("+0.000;-0.000") },
                { "{{Status}}", Escape(transaction.Status) },
                { "{{WeighedBy}}", Escape(transaction.WeighedBy) },
                { "{{WeighedDate}}", weighedDt != DateTime.MinValue ? weighedDt.ToString("dd-MM-yyyy") : Escape(transaction.WeighedDate) },
                { "{{WeighedTime}}", weighedDt != DateTime.MinValue ? weighedDt.ToString("HH.mm") : string.Empty },
                { "{{WeighedDateTime}}", weighedDt != DateTime.MinValue ? weighedDt.ToString("dd-MM-yyyy HH.mm") : Escape(transaction.WeighedDate) },
                { "{{Sequence}}", transaction.Sequence.ToString() },
                { "{{CustomerName}}", Escape(transaction.CustomerName) },
                { "{{Location}}", Escape(transaction.Location) },
                { "{{EquipmentId}}", Escape(transaction.EquipmentId) },
                { "{{TareWeight}}", transaction.TareWeight.ToString("F3") },
                { "{{Remarks}}", Escape(transaction.Remarks) },
                { "{{PrintedDate}}", now.ToString("dd-MM-yyyy") },
                { "{{PrintedTime}}", now.ToString("HH.mm") },
                { "{{PrintedDateTime}}", now.ToString("dd-MM-yyyy HH:mm:ss") },
                { "{{LowerWeight}}", transaction.LowerWeight.ToString("F3") },
                { "{{UpperWeight}}", transaction.UpperWeight.ToString("F3") },
                { "{{ManufacturingDate}}", Escape(transaction.ManufacturingDate) },
                { "{{ExpiryDate}}", Escape(transaction.ExpiryDate) },
            };

            var result = zplTemplate;
            foreach (var kvp in replacements)
            {
                result = result.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }

        /// <summary>
        /// Creates a sample WeighingTransaction with realistic data for preview purposes.
        /// </summary>
        public static WeighingTransaction CreateSampleTransaction()
        {
            var now = DateTime.Now;
            return new WeighingTransaction
            {
                Id = "sample-0001",
                BatchId = 1,
                BatchNumber = $"B{now:yyMMdd}-1",
                MaterialCode = "CHK-00001",
                MaterialName = "Chicken Drumsticks",
                Sequence = 3,
                TargetWeight = 10.000,
                LowerWeight = 9.500,
                UpperWeight = 10.500,
                ActualWeight = 10.052,
                Deviation = 0.052,
                Status = "Accept",
                WeighedBy = "Operator1",
                WeighedDate = now.ToString("yyyy-MM-dd HH:mm:ss"),
                WeighedDateValue = now,
                CustomerName = "Sample Customer",
                ManufacturingDate = now.ToString("yyyy-MM-dd"),
                ExpiryDate = now.AddDays(365).ToString("yyyy-MM-dd"),
                EquipmentId = "Scale-01",
                Location = "Production Line A",
                Remarks = "Sample label preview",
                TareWeight = 0.150
            };
        }

        /// <summary>
        /// Basic validation of a ZPL template.
        /// </summary>
        public static (bool IsValid, string ErrorMessage) ValidateTemplate(string name, string zplCode)
        {
            if (string.IsNullOrWhiteSpace(name))
                return (false, "Template name is required.");

            if (string.IsNullOrWhiteSpace(zplCode))
                return (false, "ZPL code is required.");

            if (!zplCode.Contains("^XA"))
                return (false, "ZPL code must contain ^XA (start label command).");

            if (!zplCode.Contains("^XZ"))
                return (false, "ZPL code must contain ^XZ (end label command).");

            return (true, string.Empty);
        }

        /// <summary>
        /// Escapes special ZPL characters from a value.
        /// </summary>
        private static string Escape(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text
                .Replace("^", "")
                .Replace("~", "")
                .Replace("\\", "/");
        }

        /// <summary>
        /// Returns the default ZPL template (matching the current hardcoded label in ZplPrintService)
        /// with placeholders so it can be used as a starter template.
        /// </summary>
        public static string GetDefaultZplTemplate()
        {
            var sb = new StringBuilder();

            sb.AppendLine("^XA");
            sb.AppendLine("^PW799");
            sb.AppendLine("^LL400");
            sb.AppendLine("^FO20,20^A0N,32,32^FD{{PrintTitle}} - WEIGHING LABEL^FS");
            sb.AppendLine("^FO415,10^BY3^BCN,75,Y,N,N^FD{{MaterialCode}}^FS");
            sb.AppendLine("^FO20,130^A0N,32,32^FDBatch No. : ^FS");
            sb.AppendLine("^FO200,130^A0N,32,32^FD{{BatchNumber}}^FS");
            sb.AppendLine("^FO20,220^BQN,2,3^FDLA,Category={{MaterialName}} {{TargetWeightInt}} kg|Variant={{MaterialCode}}^FS");
            sb.AppendLine("^FO400,170^A0N,38,38^FDWEIGHT :^FS");
            sb.AppendLine("^FO560,165^A0N,48,48^FD{{ActualWeightWithUnit}}^FS");
            sb.AppendLine("^FO400,230^A0N,34,34^FD{{MaterialName}}^FS");
            sb.AppendLine("^FO20,180^A0N,30,30^FDLogin - {{WeighedBy}}^FS");
            sb.AppendLine("^FO400,300^A0N,30,30^FD{{WeighedDate}}  {{WeighedTime}}^FS");
            sb.AppendLine("^XZ");

            return sb.ToString();
        }

        /// <summary>
        /// Seeds the default template if no templates exist yet.
        /// </summary>
        public async Task SeedDefaultTemplateIfEmptyAsync()
        {
            var all = await _repository.GetAllAsync();
            if (all.Count > 0)
                return;

            var defaultTemplate = new ZplLabelTemplate
            {
                Name = "Default 100�50 Weighing Label",
                Description = "Standard weighing label with barcode, QR code, weight, batch number, and operator info. 100mm � 50mm at 203 DPI.",
                ZplCode = GetDefaultZplTemplate(),
                LabelWidthMm = 100,
                LabelHeightMm = 50,
                Dpi = 203,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            await _repository.CreateAsync(defaultTemplate);
        }
    }
}

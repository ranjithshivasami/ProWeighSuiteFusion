using System;
using System.Text;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;
using System.IO;

namespace ProWeighSuitePlatforms.Services
{
    public class ZplPrintService
    {
        public ZplPrintService()
        {
        }

        public async Task<bool> PrintTransactionLabelAsync(WeighingTransaction transaction, bool isReprint = false)
        {
            try
            {
                var zplCode = await GenerateZplFromTemplateOrFallbackAsync(transaction, isReprint);
                // Send generated ZPL to printer (network or Windows raw printer). Falls back to saving file internally.
                await SendToPrinterAsync(zplCode);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Tries to load the active ZPL template from the database and resolve placeholders.
        /// Falls back to the hardcoded label if no active template exists.
        /// </summary>
        private async Task<string> GenerateZplFromTemplateOrFallbackAsync(WeighingTransaction transaction, bool isReprint = false)
        {
            try
            {
                var templateService = new ZplTemplateService();
                await templateService.InitializeAsync();
                var activeTemplate = await templateService.GetActiveAsync();

                if (activeTemplate != null && !string.IsNullOrWhiteSpace(activeTemplate.ZplCode))
                {
                    return ZplTemplateService.ResolveZpl(activeTemplate.ZplCode, transaction, isReprint);
                }
            }
            catch
            {
                // Fall through to hardcoded label on any error
            }

            return GenerateZplLabel(transaction, isReprint);
        }

        private string GenerateZplLabel(WeighingTransaction transaction, bool isReprint = false)
        {
            var sb = new StringBuilder();

            // Start label
            sb.AppendLine("^XA");

            // Set print width and length (100mm x 50mm at 203 dpi)
            sb.AppendLine("^PW799");
            sb.AppendLine("^LL400");

            // Title
            var title = isReprint ? "REPRINT - WEIGHING LABEL" : "PRINT - WEIGHING LABEL";
            sb.AppendLine($"^FO20,20^A0N,32,32^FD{title}^FS");

            // Barcode - top right (Code 128) with human readable below
            var barcodeData = EscapeZpl(transaction.MaterialCode);
            sb.AppendLine("^FO415,10^BY3^BCN,75,Y,N,N^FD" + barcodeData + "^FS");

            // Batch number
            var batch = EscapeZpl(transaction.BatchNumber);
            sb.AppendLine("^FO20,130^A0N,32,32^FDBatch No. : ^FS");
            sb.AppendLine("^FO200,130^A0N,32,32^FD" + batch + "^FS");

            // QR Code - contains Category and Variant
            var category = EscapeZpl(transaction.MaterialName);
            var variant = EscapeZpl(transaction.MaterialCode);
            var qrCategory = category;
            if (transaction.TargetWeight > 0)
            {
                qrCategory = qrCategory + " " + transaction.TargetWeight.ToString("F0") + " kg";
            }
            var qrData = "Category=" + qrCategory + "|Variant=" + variant;
            sb.AppendLine("^FO20,220^BQN,2,3^FDLA," + qrData + "^FS");

            // Weight block
            sb.AppendLine("^FO400,170^A0N,38,38^FDWEIGHT :^FS");
            sb.AppendLine("^FO560,165^A0N,48,48^FD" + transaction.ActualWeight.ToString("F3") + " kg^FS");

            // Material name under weight
            var materialName = category;
            if (materialName.Length > 20)
            {
                materialName = materialName.Substring(0, 20);
            }
            sb.AppendLine("^FO400,230^A0N,34,34^FD" + materialName.ToUpper() + "^FS");

            // Login/User - left
            sb.AppendLine("^FO20,180^A0N,30,30^FDLogin - " + EscapeZpl(transaction.WeighedBy) + "^FS");

            // Date and Time - bottom right/centered per sample (date format with dashes, time with dot)
            var dateStr = transaction.WeighedDateValue != default
                ? transaction.WeighedDateValue.ToString("dd-MM-yyyy")
                : (DateTime.TryParse(transaction.WeighedDate, out var wd) ? wd.ToString("dd-MM-yyyy") : transaction.WeighedDate);
            var timeStr = transaction.WeighedDateValue != default
                ? transaction.WeighedDateValue.ToString("HH.mm")
                : (DateTime.TryParse(transaction.WeighedDate, out var wt) ? wt.ToString("HH.mm") : string.Empty);
            sb.AppendLine("^FO400,300^A0N,30,30^FD" + dateStr + "  " + timeStr + "^FS");

            // End label
            sb.AppendLine("^XZ");

            return sb.ToString();
        }

        private string EscapeZpl(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            
            // Replace special characters that might cause issues in ZPL
            return text
                .Replace("^", "")
                .Replace("~", "")
                .Replace("\\", "/");
        }

        private async Task SendToPrinterAsync(string zplCode)
        {
            try
            {
                // Try to get printer settings from ScaleConfigService if available
                PrinterSettings? printerSettings = null;
                try
                {
                    // If ScaleConfigService later exposes printer settings, adapt here.
                    // For now attempt to load from a file or leave null.
                }
                catch { }

                // If network printing configured, attempt network
                if (printerSettings != null && !string.IsNullOrEmpty(printerSettings.PrinterIp) && printerSettings.PrinterType?.ToLower() == "network")
                {
                    await SendViaNetworkAsync(zplCode, printerSettings.PrinterIp, printerSettings.PrinterPort);
                    return;
                }

                // Otherwise try Windows raw printing
                var printerName = GetZplPrinterName();
                if (!string.IsNullOrEmpty(printerName))
                {
                    await SendRawZplToPrinter(printerName, zplCode);
                    return;
                }

                // Fallback: save to file
                await SaveToFileAsync(zplCode);
            }
            catch
            {
                await SaveToFileAsync(zplCode);
            }
        }

        private async Task SendToWindowsPrinterAsync(string zplCode)
        {
            try
            {
                // Try to find ZPL printer and send directly
                var printerName = GetZplPrinterName();
                
                if (!string.IsNullOrEmpty(printerName))
                {
                    // Send ZPL directly to printer using raw printing
                    await SendRawZplToPrinter(printerName, zplCode);
                }
                else
                {
                    // Fallback: Save to file for manual printing
                    await SaveToFileAsync(zplCode);
                }
            }
            catch (Exception)
            {
                // Fallback: Save to file
                await SaveToFileAsync(zplCode);
            }
        }

        private string? GetZplPrinterName()
        {
            try
            {
                // Get list of installed printers using System.Drawing.Printing
                foreach (string printerName in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                {
                    var name = printerName.ToLower();
                    // Look for Zebra, ZPL, or Label printer
                    if (name.Contains("zebra") || name.Contains("zpl") || name.Contains("label"))
                    {
                        return printerName;
                    }
                }
                
                // If no specific printer found, use default printer
                var defaultPrinter = new System.Drawing.Printing.PrinterSettings();
                return defaultPrinter.PrinterName;
            }
            catch
            {
                return null;
            }
        }

        private async Task SendRawZplToPrinter(string printerName, string zplCode)
        {
            await Task.Run(() =>
            {
                // Use RawPrinterHelper to send ZPL directly to printer
                RawPrinterHelper.SendStringToPrinter(printerName, zplCode);
            });
        }

        // Save ZPL to a timestamped file under AppData/ProWeighSuitePlatforms/zpl for debugging
        public async Task<string> SaveZplToFileAsync(WeighingTransaction transaction, string? outputFolder = null, bool isReprint = false)
        {
            var zpl = await GenerateZplFromTemplateOrFallbackAsync(transaction, isReprint);
            var dir = outputFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProWeighSuiteFusion", "zpl");
            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var filename = Path.Combine(dir, $"Label_{transaction.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.zpl");
                await File.WriteAllTextAsync(filename, zpl);
                return filename;
            }
            catch (Exception)
            {
                // On failure, fallback to temp path
                var tempFile = Path.Combine(Path.GetTempPath(), $"Label_{transaction.Id}_{DateTime.Now:yyyyMMdd_HHmms}.zpl");
                await File.WriteAllTextAsync(tempFile, zpl);
                return tempFile;
            }
        }

        private async Task<string> SaveToFileAsync(string zplCode)
        {
            // Save to AppData ProWeighSuitePlatforms/zpl for easier debugging
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProWeighSuiteFusion", "zpl");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var tempFile = Path.Combine(dir, $"Label_{DateTime.Now:yyyyMMdd_HHmms}.zpl");
            await File.WriteAllTextAsync(tempFile, zplCode);
            return tempFile;
        }

        // Network printing option (if printer has network interface)
        private async Task SendViaNetworkAsync(string zplCode, string printerIp = "192.168.1.100", int port = 9100)
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(printerIp, port);
            
            using var stream = client.GetStream();
            var data = Encoding.ASCII.GetBytes(zplCode);
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
        }
    }
}

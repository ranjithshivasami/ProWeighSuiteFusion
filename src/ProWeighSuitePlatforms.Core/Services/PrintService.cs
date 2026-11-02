using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class PrintService
    {
        private WeighingTransaction? _currentTransaction;
        private bool _isReprint;

        public async Task<bool> PrintTransactionLabelAsync(WeighingTransaction transaction, bool isReprint = false)
        {
            _currentTransaction = transaction;
            _isReprint = isReprint;
            
            return await Task.Run(() =>
            {
                try
                {
                    var printDocument = new PrintDocument();
                    printDocument.PrintPage += PrintDocument_PrintPage;
                    
                    // Use default printer
                    printDocument.Print();
                    
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        private void PrintDocument_PrintPage(object sender, PrintPageEventArgs e)
        {
            if (_currentTransaction == null || e.Graphics == null)
                return;

            var graphics = e.Graphics;
            var y = 20;
            var leftMargin = 20;
            var lineHeight = 20;

            // Fonts
            var titleFont = new Font("Arial", 16, FontStyle.Bold);
            var headerFont = new Font("Arial", 12, FontStyle.Bold);
            var normalFont = new Font("Arial", 10, FontStyle.Regular);

            // Title
            var title = _isReprint ? "REPRINT - WEIGHING TRANSACTION LABEL" : "PRINT - WEIGHING TRANSACTION LABEL";
            graphics.DrawString(title, titleFont, Brushes.Black, leftMargin, y);
            y += 40;

            // Batch Information
            graphics.DrawString("Batch Information", headerFont, Brushes.Black, leftMargin, y);
            y += lineHeight + 5;
            
            graphics.DrawString($"Batch Number: {_currentTransaction.BatchNumber}", normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight + 10;

            // Material Information
            graphics.DrawString("Material Information", headerFont, Brushes.Black, leftMargin, y);
            y += lineHeight + 5;
            
            graphics.DrawString($"Material: {_currentTransaction.MaterialName}", normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight;
            
            graphics.DrawString($"Material Code: {_currentTransaction.MaterialCode}", normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight;
            
            graphics.DrawString($"Sequence: {_currentTransaction.Sequence}", normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight + 10;

            // Weight Information
            graphics.DrawString("Weight Information", headerFont, Brushes.Black, leftMargin, y);
            y += lineHeight + 5;
            
            graphics.DrawString($"Target Weight: {_currentTransaction.TargetWeight:N3} kg", normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight;
            
            graphics.DrawString($"Actual Weight: {_currentTransaction.ActualWeight:N3} kg", normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight;
            
            graphics.DrawString($"Deviation: {_currentTransaction.Deviation:+0.000;-0.000} kg", normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight;
            
            var statusColor = _currentTransaction.Status == "Accept" ? Brushes.Green :
                             _currentTransaction.Status == "UnderWeight" ? Brushes.Orange :
                             Brushes.Red;
            graphics.DrawString($"Status: {_currentTransaction.Status}", normalFont, statusColor, leftMargin, y);
            y += lineHeight + 10;

            // Transaction Details
            graphics.DrawString("Transaction Details", headerFont, Brushes.Black, leftMargin, y);
            y += lineHeight + 5;
            
            graphics.DrawString($"Weighed By: {_currentTransaction.WeighedBy}", normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight;
            
            graphics.DrawString($"Weighed Date: {_currentTransaction.WeighedDate}", normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight;
            
            graphics.DrawString($"Printed Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight + 20;

            // Footer
            graphics.DrawString(new string('-', 80), normalFont, Brushes.Black, leftMargin, y);
            y += lineHeight;
            graphics.DrawString("This is a computer-generated label", new Font("Arial", 8, FontStyle.Italic), Brushes.Gray, leftMargin, y);

            // Cleanup
            titleFont.Dispose();
            headerFont.Dispose();
            normalFont.Dispose();
        }
    }
}

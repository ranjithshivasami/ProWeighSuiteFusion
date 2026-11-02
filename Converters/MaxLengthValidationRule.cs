using System.Globalization;
using System.Windows.Controls;

namespace ProWeighSuitePlatforms.Converters
{
    public class MaxLengthValidationRule : ValidationRule
    {
        public int MaxLength { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var text = value as string ?? string.Empty;
            if (text.Length > MaxLength)
            {
                return new ValidationResult(false, $"Max {MaxLength} characters allowed");
            }
            return ValidationResult.ValidResult;
        }
    }
}
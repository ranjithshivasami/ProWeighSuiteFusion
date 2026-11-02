using System;
using DotNetKit.Windows.Controls;

namespace ProWeighSuitePlatforms.ViewModels
{
    /// <summary>
    /// Custom filter for DotNetKit.Wpf.AutoCompleteComboBox that matches
    /// MaterialSuggestion on ProductCode OR ProductName (case-insensitive contains).
    /// Placeholder items ("No matches found.") are never shown - the external
    /// "No match found" TextBlock handles that state via HasNoMaterialMatches.
    /// </summary>
    public class MaterialSuggestionAutocompleteSetting : AutoCompleteComboBoxSetting
    {
        public override Predicate<object> GetFilter(string query, Func<object, string> stringFromItem)
        {
            return item =>
            {
                if (item is MaterialSuggestion suggestion)
                {
                    if (suggestion.IsPlaceholder)
                        return false;

                    // Only active products are suggested
                    if (!suggestion.IsActive)
                        return false;

                    if (string.IsNullOrWhiteSpace(query))
                        return true;

                    return suggestion.ProductCode.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || suggestion.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase);
                }

                // Fallback for any other item type
                var text = stringFromItem(item);
                return text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            };
        }

        public override TimeSpan Delay => TimeSpan.FromMilliseconds(150);

        public override int MaxSuggestionCount => 100;
    }
}

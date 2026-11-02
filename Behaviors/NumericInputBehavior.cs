using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProWeighSuitePlatforms.Behaviors
{
    public enum NumericInputMode
    {
        None,
        Integer,
        Decimal
    }

    public static class NumericInputBehavior
    {
        public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
            "Mode",
            typeof(NumericInputMode),
            typeof(NumericInputBehavior),
            new PropertyMetadata(NumericInputMode.None, OnModeChanged));

        public static NumericInputMode GetMode(DependencyObject obj) => (NumericInputMode)obj.GetValue(ModeProperty);
        public static void SetMode(DependencyObject obj, NumericInputMode value) => obj.SetValue(ModeProperty, value);

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox textBox) return;

            var mode = (NumericInputMode)e.NewValue;
            if (mode == NumericInputMode.None)
            {
                textBox.PreviewKeyDown -= OnPreviewKeyDown;
                textBox.PreviewTextInput -= OnPreviewTextInput;
                DataObject.RemovePastingHandler(textBox, OnPasting);
            }
            else
            {
                textBox.PreviewKeyDown += OnPreviewKeyDown;
                textBox.PreviewTextInput += OnPreviewTextInput;
                DataObject.AddPastingHandler(textBox, OnPasting);
            }
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Block whitespace keys from ever entering the field. Tab is intentionally
            // NOT handled here so WPF focus navigation to the next field keeps working.
            if (e.Key == Key.Space)
                e.Handled = true;
        }

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var mode = GetMode(textBox);

            var proposed = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
                                      .Insert(textBox.SelectionStart, e.Text);
            e.Handled = !IsValid(mode, proposed);
        }

        private static void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var mode = GetMode(textBox);
            if (mode == NumericInputMode.None) return;

            var dataObject = e.DataObject;
            if (!dataObject.GetDataPresent(DataFormats.UnicodeText))
                return;

            var raw = dataObject.GetData(DataFormats.UnicodeText)?.ToString() ?? string.Empty;

            // Strip all whitespace from the pasted value (e.g. "12 3" becomes "123").
            var cleaned = string.Concat(raw.Where(c => !char.IsWhiteSpace(c)));

            if (!IsValid(mode, cleaned))
            {
                e.CancelCommand();
                return;
            }

            // Replace the clipboard payload with the sanitized text so paste inserts it.
            e.DataObject.SetData(DataFormats.UnicodeText, cleaned);
        }

        private static bool IsValid(NumericInputMode mode, string text)
        {
            if (mode == NumericInputMode.Integer)
                return Regex.IsMatch(text, @"^\d*$");

            if (mode == NumericInputMode.Decimal)
                return Regex.IsMatch(text, @"^\d*[.,]?\d*$");

            return true;
        }
    }
}

using System.Windows;
using System.Windows.Controls;

namespace ProWeighSuitePlatforms.Views
{
    public partial class ZplTemplateView : UserControl
    {
        public ZplTemplateView()
        {
            InitializeComponent();
        }

        private void PlaceholderButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string token)
            {
                var editor = ZplCodeEditor;
                if (editor == null) return;

                // Remember caret position
                var caretIndex = editor.CaretIndex;
                var currentText = editor.Text ?? string.Empty;

                // Clamp caret to valid range
                if (caretIndex < 0) caretIndex = 0;
                if (caretIndex > currentText.Length) caretIndex = currentText.Length;

                // Insert the placeholder token at the cursor position
                var newText = currentText.Insert(caretIndex, token);
                editor.Text = newText;

                // Move cursor to after the inserted token
                editor.CaretIndex = caretIndex + token.Length;

                // Push updated text back to the ViewModel binding
                var binding = editor.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();

                // Return focus to the editor
                editor.Focus();
            }
        }
    }
}

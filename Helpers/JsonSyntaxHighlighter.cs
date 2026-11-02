using System;
using System.Windows.Documents;
using System.Windows.Media;

namespace ProWeighSuitePlatforms.Helpers
{
    /// <summary>
    /// Lightweight JSON syntax highlighter for displaying raw server responses.
    /// Uses a simple character scanner (no external editor library) and the VS Code
    /// Light color palette, so tokens stay readable on a white background.
    /// </summary>
    public static class JsonSyntaxHighlighter
    {
        private static readonly Brush DefaultBrush = FromHex("#383A42");
        private static readonly Brush KeyBrush = FromHex("#0451A5");
        private static readonly Brush StringBrush = FromHex("#A31515");
        private static readonly Brush NumberBrush = FromHex("#098658");
        private static readonly Brush KeywordBrush = FromHex("#0000FF");
        private static readonly Brush PunctuationBrush = FromHex("#383A42");

        private static Brush FromHex(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }

        /// <summary>
        /// Pretty-prints the response if it is valid JSON; otherwise returns it unchanged.
        /// </summary>
        public static string PrettyPrint(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                return System.Text.Json.JsonSerializer.Serialize(
                    doc.RootElement,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception)
            {
                // Not valid JSON (e.g., a plain-text error body) - show it as-is
                return raw;
            }
        }

        /// <summary>
        /// Colorizes a JSON string into <paramref name="document"/> as monospace runs.
        /// </summary>
        public static void Highlight(FlowDocument document, string json)
        {
            document.Blocks.Clear();

            if (string.IsNullOrWhiteSpace(json)) return;

            var paragraph = new Paragraph
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                LineHeight = 18
            };

            int i = 0;
            int length = json.Length;

            while (i < length)
            {
                char c = json[i];

                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (c == '"')
                {
                    int start = i;
                    i++;
                    while (i < length)
                    {
                        if (json[i] == '\\') { i += 2; continue; }
                        if (json[i] == '"') { i++; break; }
                        i++;
                    }

                    // A string followed by ':' is a JSON key, otherwise it is a value
                    int j = i;
                    while (j < length && char.IsWhiteSpace(json[j])) j++;
                    bool isKey = j < length && json[j] == ':';

                    paragraph.Inlines.Add(new Run(json.Substring(start, i - start)) { Foreground = isKey ? KeyBrush : StringBrush });
                    continue;
                }

                if (c == '-' || char.IsDigit(c))
                {
                    int start = i;
                    if (c == '-') i++;
                    while (i < length && (char.IsDigit(json[i]) || json[i] == '.' || json[i] == 'e' || json[i] == 'E' || json[i] == '+' || json[i] == '-')) i++;
                    paragraph.Inlines.Add(new Run(json.Substring(start, i - start)) { Foreground = NumberBrush });
                    continue;
                }

                if (StartsWith(json, i, "true") || StartsWith(json, i, "false") || StartsWith(json, i, "null"))
                {
                    string word = StartsWith(json, i, "false") ? "false" : StartsWith(json, i, "true") ? "true" : "null";
                    paragraph.Inlines.Add(new Run(word) { Foreground = KeywordBrush });
                    i += word.Length;
                    continue;
                }

                if (c == '{' || c == '}' || c == '[' || c == ']' || c == ':' || c == ',')
                {
                    paragraph.Inlines.Add(new Run(c.ToString()) { Foreground = PunctuationBrush });
                    i++;
                    continue;
                }

                // Unmatched text (e.g., a plain-text error body) - emit until the next token start
                int s = i;
                while (i < length && !char.IsWhiteSpace(json[i])
                    && json[i] != '"' && json[i] != '{' && json[i] != '}' && json[i] != '[' && json[i] != ']'
                    && json[i] != ':' && json[i] != ',' && json[i] != '-')
                {
                    i++;
                }
                paragraph.Inlines.Add(new Run(json.Substring(s, i - s)) { Foreground = DefaultBrush });
            }

            document.Blocks.Add(paragraph);
        }

        private static bool StartsWith(string text, int index, string word)
        {
            if (index + word.Length > text.Length) return false;
            for (int k = 0; k < word.Length; k++)
            {
                if (text[index + k] != word[k]) return false;
            }
            return true;
        }
    }
}

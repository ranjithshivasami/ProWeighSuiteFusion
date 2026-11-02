using System;
using System.Collections.Generic;
using System.Reflection;

namespace ProWeighSuitePlatforms.Data
{
    /// <summary>
    /// Global, reflection-based helper that trims leading/trailing whitespace from every
    /// string property of an entity before it is persisted. This guarantees that no entry
    /// is ever saved with accidental surrounding whitespace, regardless of how the value
    /// entered the application (typing, paste, barcode scanner, CSV import, etc.).
    ///
    /// EXCEPTIONS (properties that deliberately preserve surrounding whitespace):
    ///   - <see cref="Models.User.PasswordHash"/>            - password hashes must never be altered.
    ///   - <see cref="Models.EmailSettings.SmtpPassword"/>   - SMTP credentials must never be altered.
    ///
    /// Computed/read-only string properties are skipped automatically (no setter).
    /// </summary>
    public static class EntityTextNormalizer
    {
        private static readonly HashSet<string> SkipProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(Models.User.PasswordHash),
            nameof(Models.EmailSettings.SmtpPassword),
        };

        /// <summary>
        /// Trims every writable string property of <paramref name="entity"/> in place.
        /// </summary>
        public static void Trim(object entity)
        {
            if (entity is null) return;

            foreach (var property in entity.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.PropertyType != typeof(string) || !property.CanWrite || !property.CanRead) continue;
                if (SkipProperties.Contains(property.Name)) continue;

                if (property.GetValue(entity) is not string value) continue;

                var trimmed = value.Trim();
                if (!string.Equals(value, trimmed, StringComparison.Ordinal))
                    property.SetValue(entity, trimmed);
            }
        }
    }
}

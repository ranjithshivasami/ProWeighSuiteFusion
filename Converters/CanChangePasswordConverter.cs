using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Converters
{
    /// <summary>
    /// Multi-value converter that returns Visibility.Visible only when the logged-in user
    /// is allowed to change the target (row) user's password.
    ///
    /// Rules:
    ///   - SuperAdmin password ? only another SuperAdmin can change it
    ///   - Admin / Operator password ? SuperAdmin or Admin can change it
    ///
    /// Bindings (in order):
    ///   [0] CurrentUser  (User?) � the logged-in user
    ///   [1] TargetUser   (User?) � the row user whose password might be changed
    /// </summary>
    public class CanChangePasswordConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return Visibility.Collapsed;

            var currentUser = values[0] as User;
            var targetUser  = values[1] as User;

            if (currentUser == null || targetUser == null)
                return Visibility.Collapsed;

            bool allowed;

            // SuperAdmin password can only be changed by another SuperAdmin
            if (targetUser.Role == UserRole.SuperAdmin)
                allowed = currentUser.Role == UserRole.SuperAdmin;
            else
                allowed = currentUser.Role == UserRole.SuperAdmin
                       || currentUser.Role == UserRole.Admin;

            return allowed ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

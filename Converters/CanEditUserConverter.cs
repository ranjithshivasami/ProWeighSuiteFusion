using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Converters
{
    /// <summary>
    /// Multi-value converter that returns Visibility.Visible only when the logged-in user
    /// is allowed to edit the target (row) user's account.
    ///
    /// Rules:
    ///   - The logged-in user must hold the EditUser permission.
    ///   - A SuperAdmin may only edit another SuperAdmin account.
    ///   - A non-SuperAdmin may only edit non-SuperAdmin accounts (never SuperAdmins).
    ///
    /// Bindings (in order):
    ///   [0] CurrentUser  (User?) - the logged-in user
    ///   [1] TargetUser   (User?) - the row user being edited
    ///   [2] CanEditUser  (bool)  - whether the EditUser permission is granted
    /// </summary>
    public class CanEditUserConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3)
                return Visibility.Collapsed;

            var currentUser = values[0] as User;
            var targetUser  = values[1] as User;

            if (currentUser == null || targetUser == null)
                return Visibility.Collapsed;

            if (!(values[2] is bool canEdit) || !canEdit)
                return Visibility.Collapsed;

            var currentIsSuperAdmin = currentUser.Role == UserRole.SuperAdmin;
            var targetIsSuperAdmin  = targetUser.Role == UserRole.SuperAdmin;

            // Edit is allowed only when both users share the same "class" (SuperAdmin vs regular).
            return currentIsSuperAdmin == targetIsSuperAdmin
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class BaseViewModel : INotifyPropertyChanged
    {
        protected readonly AuthorizationService AuthorizationService = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(propertyName);
            return true;
        }

        protected bool HasPermission(User? user, Permission permission)
        {
            return AuthorizationService.HasPermission(user, permission);
        }

        protected bool HasAnyPermission(User? user, params Permission[] permissions)
        {
            return AuthorizationService.HasAnyPermission(user, permissions);
        }

        protected bool HasAllPermissions(User? user, params Permission[] permissions)
        {
            return AuthorizationService.HasAllPermissions(user, permissions);
        }

        protected void CheckPermissionOrThrow(User? user, Permission permission)
        {
            if (!HasPermission(user, permission))
            {
                throw new UnauthorizedAccessException($"User does not have {permission} permission");
            }
        }
    }
}
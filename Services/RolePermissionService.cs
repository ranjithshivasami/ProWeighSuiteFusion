using System.Collections.Generic;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class RolePermissionService
    {
        private readonly RolePermissionRepository _repository;

        public RolePermissionService(RolePermissionRepository repository)
        {
            _repository = repository;
        }

        public async Task InitializeAsync()
        {
            await _repository.InitializeAsync();
        }

        public async Task<List<RolePermission>> GetAllAsync()
        {
            return await _repository.GetAllAsync();
        }

        public async Task<Dictionary<UserRole, HashSet<Permission>>> GetRolePermissionsMapAsync()
        {
            return await _repository.GetRolePermissionsMapAsync();
        }

        public async Task<int> UpdatePermissionAsync(UserRole role, Permission permission, bool isGranted, string modifiedBy)
        {
            return await _repository.UpdatePermissionAsync(role, permission, isGranted, modifiedBy);
        }

        public async Task<int> UpdateMultiplePermissionsAsync(List<(UserRole role, Permission permission, bool isGranted)> updates, string modifiedBy)
        {
            return await _repository.UpdateMultiplePermissionsAsync(updates, modifiedBy);
        }

        public async Task<bool> ResetToDefaultsAsync()
        {
            return await _repository.ResetToDefaultsAsync();
        }
    }
}

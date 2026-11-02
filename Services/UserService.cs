using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public interface IUserService
    {
        Task InitializeAsync();
        Task<User?> GetUserAsync(int id);
        Task<User?> GetUserByUsernameAsync(string username);
        Task<IEnumerable<User>> GetAllUsersAsync();
        Task<int> CreateUserAsync(User user, string password);
        Task<int> UpdateUserAsync(User user, string? newPassword = null);
        Task<int> DeleteUserAsync(int id);
        Task<bool> UsernameExistsAsync(string username, int? excludeUserId = null);
        Task<bool> EmailExistsAsync(string email, int? excludeUserId = null);
        Task<bool> ChangePasswordAsync(int userId, string newPassword);
        Task<int> GetUserCountAsync();
        string HashPassword(string password);
        bool VerifyPassword(string password, string hash);
    }

    public class UserService : IUserService
    {
        private readonly IUserRepository _repository;

        public UserService(IUserRepository repository)
        {
            _repository = repository;
        }

        public async Task InitializeAsync()
        {
            await _repository.InitializeAsync();
        }

        public async Task<User?> GetUserAsync(int id)
        {
            return await _repository.GetAsync(id);
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            return await _repository.GetByUsernameAsync(username);
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _repository.GetAllAsync();
        }

        public async Task<int> CreateUserAsync(User user, string password)
        {
            if (string.IsNullOrWhiteSpace(user.Username))
                throw new ArgumentException("Username cannot be empty", nameof(user.Username));

            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be empty", nameof(password));

            if (await _repository.UsernameExistsAsync(user.Username))
                throw new InvalidOperationException($"Username '{user.Username}' already exists");

            if (string.IsNullOrWhiteSpace(user.Email))
                throw new ArgumentException("Email cannot be empty", nameof(user.Email));

            if (await _repository.EmailExistsAsync(user.Email))
                throw new InvalidOperationException($"Email '{user.Email}' already exists");

            user.PasswordHash = HashPassword(password);
            user.CreatedAt = DateTime.Now;
            user.UpdatedAt = DateTime.Now;
            
            return await _repository.CreateAsync(user);
        }

        public async Task<int> UpdateUserAsync(User user, string? newPassword = null)
        {
            if (string.IsNullOrWhiteSpace(user.Username))
                throw new ArgumentException("Username cannot be empty", nameof(user.Username));

            if (await _repository.UsernameExistsAsync(user.Username, user.Id))
                throw new InvalidOperationException($"Username '{user.Username}' already exists");

            if (string.IsNullOrWhiteSpace(user.Email))
                throw new ArgumentException("Email cannot be empty", nameof(user.Email));

            if (await _repository.EmailExistsAsync(user.Email, user.Id))
                throw new InvalidOperationException($"Email '{user.Email}' already exists");

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                user.PasswordHash = HashPassword(newPassword);
            }

            user.UpdatedAt = DateTime.Now;
            
            return await _repository.UpdateAsync(user);
        }

        public async Task<int> DeleteUserAsync(int id)
        {
            var user = await _repository.GetAsync(id);
            if (user == null)
                throw new InvalidOperationException("User not found");

            // Prevent deleting the last SuperAdmin
            if (user.Role == UserRole.SuperAdmin)
            {
                var allUsers = await _repository.GetAllAsync();
                var superAdminCount = allUsers.Count(u => u.Role == UserRole.SuperAdmin);
                
                if (superAdminCount <= 1)
                    throw new InvalidOperationException("Cannot delete the last Super Admin user");
            }

            return await _repository.DeleteAsync(id);
        }

        public async Task<bool> UsernameExistsAsync(string username, int? excludeUserId = null)
        {
            return await _repository.UsernameExistsAsync(username, excludeUserId);
        }

        public async Task<bool> EmailExistsAsync(string email, int? excludeUserId = null)
        {
            return await _repository.EmailExistsAsync(email, excludeUserId);
        }

        public async Task<bool> ChangePasswordAsync(int userId, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new ArgumentException("Password cannot be empty", nameof(newPassword));

            var user = await _repository.GetAsync(userId);
            if (user == null)
                return false;

            user.PasswordHash = HashPassword(newPassword);
            user.UpdatedAt = DateTime.Now;
            
            var result = await _repository.UpdateAsync(user);
            return result > 0;
        }

        public async Task<int> GetUserCountAsync()
        {
            return await _repository.GetUserCountAsync();
        }

        public string HashPassword(string password)
        {
            // For demo purposes, using SHA256
            // For production, use BCrypt.Net or Argon2
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        public bool VerifyPassword(string password, string hash)
        {
            var computedHash = HashPassword(password);
            return computedHash == hash;
        }
    }
}

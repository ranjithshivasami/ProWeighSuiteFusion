using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using Xunit;

namespace ProWeighSuitePlatforms.Tests;

public class UserServiceTests
{
    [Fact]
    public async Task CreateUserAsync_HashesPasswordAndSetsTimestamps()
    {
        var repository = new FakeUserRepository();
        var service = new UserService(repository);
        var user = new User
        {
            Username = "operator1",
            Email = "operator1@example.com",
            Role = UserRole.Operator
        };
        var before = DateTime.Now;

        var id = await service.CreateUserAsync(user, "secret");

        Assert.Equal(1, id);
        Assert.NotEqual("secret", user.PasswordHash);
        Assert.True(service.VerifyPassword("secret", user.PasswordHash));
        Assert.InRange(user.CreatedAt, before, DateTime.Now);
        Assert.InRange(user.UpdatedAt, before, DateTime.Now);
        Assert.Same(user, repository.LastCreated);
    }

    [Fact]
    public async Task CreateUserAsync_RejectsDuplicateUsernameCaseInsensitively()
    {
        var repository = new FakeUserRepository();
        var service = new UserService(repository);
        await service.CreateUserAsync(
            new User { Username = "admin", Email = "admin@example.com" },
            "secret");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateUserAsync(
                new User { Username = "ADMIN", Email = "second@example.com" },
                "secret"));

        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public async Task CreateUserAsync_RequiresUsernamePasswordAndEmail()
    {
        var service = new UserService(new FakeUserRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateUserAsync(new User { Email = "user@example.com" }, "secret"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateUserAsync(new User { Username = "user", Email = "user@example.com" }, " "));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateUserAsync(new User { Username = "user" }, "secret"));
    }

    [Fact]
    public async Task UpdateUserAsync_WithoutNewPasswordKeepsExistingHash()
    {
        var repository = new FakeUserRepository();
        var service = new UserService(repository);
        var user = new User
        {
            Username = "operator1",
            Email = "operator1@example.com",
            Role = UserRole.Operator
        };
        await service.CreateUserAsync(user, "secret");
        var originalHash = user.PasswordHash;

        user.Email = "updated@example.com";
        await service.UpdateUserAsync(user);

        Assert.Equal(originalHash, user.PasswordHash);
        Assert.True(service.VerifyPassword("secret", user.PasswordHash));
    }

    [Fact]
    public async Task UpdateUserAsync_WithNewPasswordRehashesPassword()
    {
        var repository = new FakeUserRepository();
        var service = new UserService(repository);
        var user = new User
        {
            Username = "operator1",
            Email = "operator1@example.com",
            Role = UserRole.Operator
        };
        await service.CreateUserAsync(user, "secret");
        var originalHash = user.PasswordHash;

        await service.UpdateUserAsync(user, "new-secret");

        Assert.NotEqual(originalHash, user.PasswordHash);
        Assert.True(service.VerifyPassword("new-secret", user.PasswordHash));
        Assert.False(service.VerifyPassword("secret", user.PasswordHash));
    }

    [Fact]
    public async Task DeleteUserAsync_PreventsRemovingLastSuperAdmin()
    {
        var repository = new FakeUserRepository();
        var service = new UserService(repository);
        var superAdmin = new User
        {
            Username = "root",
            Email = "root@example.com",
            Role = UserRole.SuperAdmin
        };
        await service.CreateUserAsync(superAdmin, "secret");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteUserAsync(superAdmin.Id));
        Assert.Contains("last Super Admin", exception.Message);

        var secondSuperAdmin = new User
        {
            Username = "root2",
            Email = "root2@example.com",
            Role = UserRole.SuperAdmin
        };
        await service.CreateUserAsync(secondSuperAdmin, "secret");

        Assert.Equal(1, await service.DeleteUserAsync(superAdmin.Id));
        Assert.Null(await service.GetUserAsync(superAdmin.Id));
    }

    [Fact]
    public async Task ChangePasswordAsync_ReturnsFalseWhenUserDoesNotExist()
    {
        var service = new UserService(new FakeUserRepository());

        Assert.False(await service.ChangePasswordAsync(42, "new-secret"));
    }

    [Fact]
    public void VerifyPassword_RejectsWrongPassword()
    {
        var service = new UserService(new FakeUserRepository());
        var hash = service.HashPassword("secret");

        Assert.True(service.VerifyPassword("secret", hash));
        Assert.False(service.VerifyPassword("Secret", hash));
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users = new();
        private int _nextId = 1;

        public User? LastCreated { get; private set; }

        public Task<int> CreateAsync(User user)
        {
            user.Id = _nextId++;
            _users.Add(user);
            LastCreated = user;
            return Task.FromResult(user.Id);
        }

        public Task<User?> GetAsync(int id) =>
            Task.FromResult(_users.SingleOrDefault(user => user.Id == id));

        public Task<User?> GetByUsernameAsync(string username) =>
            Task.FromResult(_users.SingleOrDefault(user =>
                string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase)));

        public Task<IEnumerable<User>> GetAllAsync() =>
            Task.FromResult<IEnumerable<User>>(_users.ToList());

        public Task<int> UpdateAsync(User user)
        {
            var index = _users.FindIndex(existing => existing.Id == user.Id);
            if (index < 0)
                return Task.FromResult(0);

            _users[index] = user;
            return Task.FromResult(1);
        }

        public Task<int> DeleteAsync(int id) =>
            Task.FromResult(_users.RemoveAll(user => user.Id == id));

        public Task<bool> UsernameExistsAsync(string username, int? excludeUserId = null) =>
            Task.FromResult(_users.Any(user =>
                user.Id != excludeUserId &&
                string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase)));

        public Task<bool> EmailExistsAsync(string email, int? excludeUserId = null) =>
            Task.FromResult(_users.Any(user =>
                user.Id != excludeUserId &&
                string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task<int> GetUserCountAsync() => Task.FromResult(_users.Count);

        public Task InitializeAsync() => Task.CompletedTask;
    }
}

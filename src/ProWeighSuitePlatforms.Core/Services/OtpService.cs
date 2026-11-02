using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Services
{
    public enum OtpVerifyResult
    {
        Success,
        Invalid,
        Expired,
        TooManyAttempts
    }

    /// <summary>
    /// Generates, stores and verifies one-time passwords used by the Super Admin OTP login flow.
    ///
    /// Rules:
    ///   - Each OTP has a lifetime of exactly 5 minutes from the time of generation.
    ///   - An OTP is invalidated immediately after a successful verification.
    ///   - An OTP is invalidated after a configurable number of failed attempts (default 5).
    ///   - Codes are kept in memory only; they are never persisted to disk.
    /// </summary>
    public class OtpService
    {
        private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);

        /// <summary>Maximum number of failed attempts before an OTP is invalidated.</summary>
        public int MaxFailedAttempts { get; set; } = 5;

        private sealed class OtpRecord
        {
            public required string Code { get; init; }
            public required DateTime ExpiresAtUtc { get; init; }
            public int FailedAttempts { get; set; }
        }

        private readonly ConcurrentDictionary<int, OtpRecord> _records = new();

        /// <summary>
        /// Generates a new 6-digit OTP for the given user and invalidates any previous code.
        /// </summary>
        public string GenerateOtp(int userId)
        {
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _records[userId] = new OtpRecord
            {
                Code = code,
                ExpiresAtUtc = DateTime.UtcNow.Add(OtpLifetime)
            };
            return code;
        }

        /// <summary>
        /// Verifies the supplied code for the user. Returns <see cref="OtpVerifyResult.Success"/>
        /// and consumes the OTP if valid, or a failure reason otherwise.
        /// </summary>
        public OtpVerifyResult VerifyOtp(int userId, string? enteredCode)
        {
            if (!_records.TryGetValue(userId, out var record))
                return OtpVerifyResult.Invalid;

            if (record.FailedAttempts >= MaxFailedAttempts)
            {
                _records.TryRemove(userId, out _);
                return OtpVerifyResult.TooManyAttempts;
            }

            if (DateTime.UtcNow > record.ExpiresAtUtc)
            {
                _records.TryRemove(userId, out _);
                return OtpVerifyResult.Expired;
            }

            if (string.Equals(record.Code, enteredCode?.Trim(), StringComparison.Ordinal))
            {
                _records.TryRemove(userId, out _);
                return OtpVerifyResult.Success;
            }

            record.FailedAttempts++;
            if (record.FailedAttempts >= MaxFailedAttempts)
            {
                _records.TryRemove(userId, out _);
                return OtpVerifyResult.TooManyAttempts;
            }

            return OtpVerifyResult.Invalid;
        }

        /// <summary>
        /// Immediately invalidates any active OTP for the user.
        /// </summary>
        public void Invalidate(int userId)
        {
            _records.TryRemove(userId, out _);
        }

        public async Task<bool> SendOtpEmailAsync(string toEmail, string username, string otpCode)
        {
            var emailService = new EmailService();
            var body = $"Hello {username},\n\n" +
                       $"Your one-time password (OTP) for signing in as Super Admin is:\n\n" +
                       $"{otpCode}\n\n" +
                       $"This OTP is valid for 5 minutes from the time it was generated.\n" +
                       $"For security, do not share this code with anyone.\n\n" +
                       "If you did not request this code, please ignore this email.";

            var emailSettings = await new EmailSettingsService().LoadAsync();
            var appName = string.IsNullOrWhiteSpace(emailSettings.AppName)
                ? "Pro Weigh Suite"
                : emailSettings.AppName.Trim();
            var subject = $"{appName} - Super Admin Login OTP";

            await emailService.SendEmailAsync(toEmail, subject, body);
            return true;
        }
    }
}

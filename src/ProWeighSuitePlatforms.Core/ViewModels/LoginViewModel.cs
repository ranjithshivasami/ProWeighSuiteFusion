using System;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class LoginViewModel : BaseViewModel
    {
        private readonly MainViewModel _main;
        private readonly AuthService _authService = new AuthService();
        private readonly OtpService _otpService = new OtpService();
        private readonly AuditService _auditService = new AuditService();
        private readonly AppSettingsService _appSettingsService = new AppSettingsService();
        private User? _pendingOtpUser;

        private string _username = "";
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value?.Trim() ?? string.Empty);
        }

        private string _password = "";
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private bool _hasTriedLogin;
        public bool HasTriedLogin
        {
            get => _hasTriedLogin;
            set => SetProperty(ref _hasTriedLogin, value);
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                    RaisePropertyChanged(nameof(CanVerify));
            }
        }

        private bool _isOtpStep;
        public bool IsOtpStep
        {
            get => _isOtpStep;
            private set => SetProperty(ref _isOtpStep, value);
        }

        private string _otpCode = string.Empty;
        public string OtpCode
        {
            get => _otpCode;
            set => SetProperty(ref _otpCode, value);
        }

        private string _otpMessage = string.Empty;
        public string OtpMessage
        {
            get => _otpMessage;
            set => SetProperty(ref _otpMessage, value);
        }

        private string _timeRemaining = "05:00";
        public string TimeRemaining
        {
            get => _timeRemaining;
            set => SetProperty(ref _timeRemaining, value);
        }

        private bool _otpExpired;
        public bool OtpExpired
        {
            get => _otpExpired;
            private set
            {
                if (SetProperty(ref _otpExpired, value))
                    RaisePropertyChanged(nameof(CanVerify));
            }
        }

        public bool CanVerify => !IsProcessing && !OtpExpired;

        private readonly DispatcherTimer _otpTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private int _otpRemainingSeconds = 5 * 60;

        public ICommand LoginCommand { get; }
        public ICommand ResendOtpCommand { get; }
        public ICommand CancelOtpCommand { get; }

        public string Title => "Login";

        public LoginViewModel(MainViewModel main)
        {
            _main = main;
            LoginCommand = new RelayCommand(async _ => await LoginAsync());
            ResendOtpCommand = new RelayCommand(async _ => await StartOtpFlowAsync(_pendingOtpUser), _ => _pendingOtpUser != null);
            CancelOtpCommand = new RelayCommand(_ => CancelOtp());
            _otpTimer.Tick += OnOtpTimerTick;
        }

        private async Task LoginAsync()
        {
            if (IsProcessing) return;

            HasTriedLogin = true;
            ErrorMessage = string.Empty;
            IsProcessing = true;

            if (IsOtpStep)
            {
                await VerifyOtpAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                // Required validation; don't call service when missing
                ErrorMessage = "Please fill in required fields.";
                IsProcessing = false;
                return;
            }

            // Read the EnableOTP setting at login time so recent changes from the
            // App Settings page take effect immediately (not just at app startup).
            var appSettings = await _appSettingsService.LoadAsync();
            var enableOtp = appSettings.EnableOTP;

            // Look up the account first - Super Admins use the OTP flow only when
            // OTP is enabled in App Settings.
            var lookup = await _authService.GetUserByUsernameAsync(Username);
            if (enableOtp && lookup != null && lookup.Role == UserRole.SuperAdmin)
            {
                await StartOtpFlowAsync(lookup);
                return;
            }

            var user = await _authService.AuthenticateAsync(Username, Password);
            if (user != null)
            {
                // Log successful login
                await _auditService.LogLoginAsync(Username, success: true);
                
                _main.CurrentUser = user;
                ErrorMessage = string.Empty;
                HasTriedLogin = false;
                IsProcessing = false;
                _main.NavigateTo(new ShellViewModel(_main, _main.HostContext));
            }
            else
            {
                // Log failed login attempt
                await _auditService.LogLoginAsync(Username, success: false);
                
                ErrorMessage = "Invalid username or password.";
                IsProcessing = false;
            }
        }

        private async Task StartOtpFlowAsync(User? user)
        {
            if (user == null)
            {
                IsProcessing = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                ErrorMessage = "No email address is configured for this Super Admin account. Please ask an administrator to set one.";
                IsProcessing = false;
                return;
            }

            var otpCode = _otpService.GenerateOtp(user.Id);
            try
            {
                await _otpService.SendOtpEmailAsync(user.Email, user.Username, otpCode);
            }
            catch (Exception ex)
            {
                _otpService.Invalidate(user.Id);
                ErrorMessage = $"Unable to send the OTP email. {ex.Message}";
                IsProcessing = false;
                return;
            }

            _pendingOtpUser = user;
            Password = string.Empty;
            OtpCode = string.Empty;
            OtpMessage = $"A one-time password has been sent to the email registered for '{user.Username}'. " +
                         "It is valid for 5 minutes.";
            IsOtpStep = true;
            ErrorMessage = string.Empty;
            HasTriedLogin = false;
            IsProcessing = false;
            StartOtpTimer();
        }

        private void StartOtpTimer()
        {
            _otpTimer.Stop();
            _otpRemainingSeconds = 5 * 60;
            OtpExpired = false;
            TimeRemaining = FormatTime(_otpRemainingSeconds);
            _otpTimer.Start();
        }

        private void StopOtpTimer()
        {
            _otpTimer.Stop();
        }

        private void OnOtpTimerTick(object? sender, EventArgs e)
        {
            _otpRemainingSeconds--;
            if (_otpRemainingSeconds <= 0)
            {
                _otpRemainingSeconds = 0;
                _otpTimer.Stop();
                OtpExpired = true;
                TimeRemaining = FormatTime(0);
                ErrorMessage = "OTP has expired. Please request a new one.";
                if (_pendingOtpUser != null)
                    _otpService.Invalidate(_pendingOtpUser.Id);
                return;
            }

            TimeRemaining = FormatTime(_otpRemainingSeconds);
        }

        private static string FormatTime(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";

        private async Task VerifyOtpAsync()
        {
            if (_pendingOtpUser == null)
            {
                CancelOtp();
                return;
            }

            if (string.IsNullOrWhiteSpace(OtpCode))
            {
                ErrorMessage = "Please enter the OTP.";
                return;
            }

            var result = _otpService.VerifyOtp(_pendingOtpUser.Id, OtpCode);
            switch (result)
            {
                case OtpVerifyResult.Success:
                    // Log successful login
                    await _auditService.LogLoginAsync(_pendingOtpUser.Username, success: true);

                    _main.CurrentUser = _pendingOtpUser;
                    _pendingOtpUser = null;
                    ErrorMessage = string.Empty;
                    HasTriedLogin = false;
                    IsOtpStep = false;
                    IsProcessing = false;
                    StopOtpTimer();
                    _main.NavigateTo(new ShellViewModel(_main, _main.HostContext));
                    break;

                case OtpVerifyResult.Expired:
                    await _auditService.LogLoginAsync(_pendingOtpUser.Username, success: false);
                    ErrorMessage = "The OTP has expired. Please request a new one.";
                    CancelOtp();
                    break;

                case OtpVerifyResult.TooManyAttempts:
                    await _auditService.LogLoginAsync(_pendingOtpUser.Username, success: false);
                    ErrorMessage = "Too many failed attempts. The OTP has been invalidated. Please sign in again.";
                    CancelOtp();
                    break;

                default:
                    await _auditService.LogLoginAsync(_pendingOtpUser.Username, success: false);
                    ErrorMessage = "Invalid OTP. Please try again.";
                    OtpCode = string.Empty;
                    IsProcessing = false;
                    break;
            }
        }

        private void CancelOtp()
        {
            StopOtpTimer();
            if (_pendingOtpUser != null)
                _otpService.Invalidate(_pendingOtpUser.Id);
            _pendingOtpUser = null;
            IsOtpStep = false;
            OtpCode = string.Empty;
            OtpMessage = string.Empty;
            HasTriedLogin = false;
            IsProcessing = false;
        }
    }
}

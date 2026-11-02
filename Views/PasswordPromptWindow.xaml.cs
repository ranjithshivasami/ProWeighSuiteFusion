using System;
using System.Windows;
using ProWeighSuitePlatforms.Services;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Views
{
    public partial class PasswordPromptWindow : Window
    {
        public string? EnteredPassword { get; private set; }

        public PasswordPromptWindow(string message = "Please enter your password to confirm.")
        {
            InitializeComponent();
            MessageText.Text = message;
            PasswordBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            EnteredPassword = PasswordBox.Password;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            EnteredPassword = null;
            DialogResult = false;
            Close();
        }

        // Changed to accept the username of the account to validate against.
        public static async Task<bool> ConfirmPasswordAsync(Window owner, string usernameToValidate)
        {
            var prompt = new PasswordPromptWindow();
            bool? result;
            try
            {
                prompt.Owner = owner;
                result = prompt.ShowDialog();
            }
            catch (InvalidOperationException)
            {
                // Owner is closing / app shutting down — treat as cancelled, never
                // let the confirm dialog itself crash the exit path.
                return false;
            }
            if (result != true) return false;

            var auth = new AuthService();
            var user = await auth.AuthenticateAsync(usernameToValidate ?? string.Empty, prompt.EnteredPassword ?? string.Empty);
            return user != null;
        }
    }
}
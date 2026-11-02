using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProWeighSuitePlatforms.Views
{
    public partial class ScrollableMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public ScrollableMessageBox(string message, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            InitializeComponent();

            Title = caption;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = (System.Windows.ResizeMode)3;

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = caption,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(titleBlock, 0);
            grid.Children.Add(titleBlock);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var msgBox = new TextBox
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontSize = 13
            };
            scrollViewer.Content = msgBox;
            Grid.SetRow(scrollViewer, 1);
            grid.Children.Add(scrollViewer);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            var yesBtn = new Button { Content = "Yes", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var noBtn = new Button { Content = "No", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancelBtn = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            var okBtn = new Button { Content = "OK", Width = 80, IsDefault = true };

            yesBtn.Click += (_, _) => { Result = MessageBoxResult.Yes; DialogResult = true; };
            noBtn.Click += (_, _) => { Result = MessageBoxResult.No; DialogResult = true; };
            cancelBtn.Click += (_, _) => { Result = MessageBoxResult.Cancel; DialogResult = true; };
            okBtn.Click += (_, _) => { Result = MessageBoxResult.OK; DialogResult = true; };

            switch (button)
            {
                case MessageBoxButton.YesNoCancel:
                    buttonPanel.Children.Add(yesBtn);
                    buttonPanel.Children.Add(noBtn);
                    buttonPanel.Children.Add(cancelBtn);
                    break;
                case MessageBoxButton.YesNo:
                    buttonPanel.Children.Add(yesBtn);
                    buttonPanel.Children.Add(noBtn);
                    break;
                case MessageBoxButton.OKCancel:
                    buttonPanel.Children.Add(okBtn);
                    buttonPanel.Children.Add(cancelBtn);
                    break;
                default:
                    buttonPanel.Children.Add(okBtn);
                    break;
            }

            Content = grid;
        }

        public static MessageBoxResult Show(string message, string caption = "",
            MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            var owner = Application.Current?.MainWindow;
            var box = new ScrollableMessageBox(message, caption, button, icon);
            box.Owner = owner;
            box.WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen;
            box.ShowDialog();
            return box.Result;
        }
    }
}

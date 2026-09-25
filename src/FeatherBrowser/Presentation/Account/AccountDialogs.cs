using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FeatherBrowser.Features.Sync;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Account;

internal static class AccountDialogs
{
    public static AccountCredentials? ShowCredentials(Window owner, bool createAccount)
    {
        var window = new Window
        {
            Owner = owner,
            Title = createAccount ? "Create Feather account" : "Sign in to Feather",
            Width = 430,
            Height = createAccount ? 365 : 315,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(12, 18, 25)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (createAccount)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = createAccount ? "Create your Feather account" : "Welcome back",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 18)
        };
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var emailPanel = Field("Email", out TextBox email);
        Grid.SetRow(emailPanel, 1);
        root.Children.Add(emailPanel);

        var passwordPanel = PasswordField("Password", out PasswordBox password);
        Grid.SetRow(passwordPanel, 2);
        root.Children.Add(passwordPanel);

        PasswordBox? confirm = null;
        if (createAccount)
        {
            var confirmPanel = PasswordField("Confirm password", out confirm);
            Grid.SetRow(confirmPanel, 3);
            root.Children.Add(confirmPanel);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = Button("Cancel", false);
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(cancel);

        var submit = Button(createAccount ? "Create account" : "Sign in", true);
        buttons.Children.Add(submit);

        int buttonRow = createAccount ? 5 : 4;
        Grid.SetRow(buttons, buttonRow);
        root.Children.Add(buttons);

        AccountCredentials? result = null;
        submit.Click += (_, _) =>
        {
            string emailValue = email.Text.Trim();
            string passwordValue = password.Password;

            if (emailValue.Length == 0 || passwordValue.Length == 0)
            {
                MessageBox.Show(window, "Enter your email and password.", "Feather Account", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (createAccount && confirm is not null && passwordValue != confirm.Password)
            {
                MessageBox.Show(window, "The passwords do not match.", "Feather Account", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            result = new AccountCredentials(emailValue, passwordValue);
            password.Clear();
            confirm?.Clear();
            window.DialogResult = true;
        };

        window.Content = root;
        window.Loaded += (_, _) =>
        {
            email.Focus();
            Keyboard.Focus(email);
        };

        return window.ShowDialog() == true ? result : null;
    }

    public static string? ShowCode(Window owner, string title, string description, bool recoveryCode = false)
    {
        var window = new Window
        {
            Owner = owner,
            Title = title,
            Width = 430,
            Height = 260,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(12, 18, 25)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(145, 160, 178)),
            Margin = new Thickness(0, 0, 0, 18)
        });

        var input = new PasswordBox
        {
            Height = 38,
            Padding = new Thickness(10, 7, 10, 7),
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(8, 13, 19)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 63, 82))
        };
        root.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = Button("Cancel", false);
        var submit = Button(recoveryCode ? "Recover" : "Verify", true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(submit);
        root.Children.Add(buttons);

        string? result = null;
        cancel.Click += (_, _) => window.DialogResult = false;
        submit.Click += (_, _) =>
        {
            string value = input.Password.Trim();
            if (value.Length == 0)
                return;

            if (!recoveryCode)
            {
                value = new string(value.Where(char.IsDigit).ToArray());
                if (value.Length != 6)
                {
                    MessageBox.Show(window, "Enter the six-digit code from your authenticator app.", "Feather Account", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            result = value;
            input.Clear();
            window.DialogResult = true;
        };

        window.Content = root;
        input.Focus();
        return window.ShowDialog() == true ? result : null;
    }

    public static void ShowRecoveryKey(Window owner, string recoveryKey)
    {
        var window = new Window
        {
            Owner = owner,
            Title = "Feather recovery key",
            Width = 560,
            Height = 335,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(12, 18, 25)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = "Save your recovery key",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "This key can restore your encrypted Feather Sync data if every approved device is lost. Feather cannot recreate it for you.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(145, 160, 178)),
            Margin = new Thickness(0, 0, 0, 16)
        });

        var key = new TextBox
        {
            Text = recoveryKey,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            MinHeight = 78,
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromRgb(8, 13, 19)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 63, 82))
        };
        root.Children.Add(key);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var copy = Button("Copy", false);
        var save = Button("Save encrypted backup", false);
        var close = Button("I saved it", true);
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(recoveryKey);
            }
            catch
            {
            }
        };
        save.Click += (_, _) => SaveRecoveryBackup(window, recoveryKey);
        close.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(copy);
        buttons.Children.Add(save);
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        window.Content = root;
        window.ShowDialog();
    }

    public static string? LoadRecoveryBackup(Window owner)
    {
        var picker = new OpenFileDialog
        {
            Title = "Open Feather recovery backup",
            Filter = "Feather recovery backup (*.feather-recovery)|*.feather-recovery|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (picker.ShowDialog(owner) != true)
            return null;

        string? password = ShowBackupPassword(
            owner,
            "Unlock recovery backup",
            "Enter the password used when this recovery backup was created.",
            false);

        if (password is null)
            return null;

        try
        {
            return new RecoveryBackupService().Load(picker.FileName, password);
        }
        catch (Exception exception) when (exception is SyncSecurityException or InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, exception.Message, "Feather recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private static void SaveRecoveryBackup(Window owner, string recoveryKey)
    {
        string? password = ShowBackupPassword(
            owner,
            "Protect recovery backup",
            "Create a separate password for this backup. Use at least 12 characters and do not reuse your Feather account password.",
            true);

        if (password is null)
            return;

        var picker = new SaveFileDialog
        {
            Title = "Save Feather recovery backup",
            Filter = "Feather recovery backup (*.feather-recovery)|*.feather-recovery",
            FileName = "Feather-Recovery.feather-recovery",
            AddExtension = true,
            DefaultExt = ".feather-recovery",
            OverwritePrompt = true
        };

        if (picker.ShowDialog(owner) != true)
            return;

        try
        {
            new RecoveryBackupService().Save(picker.FileName, recoveryKey, password);
            MessageBox.Show(
                owner,
                "The encrypted recovery backup was saved. Keep the backup password separate from the backup file.",
                "Feather recovery",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            MessageBox.Show(owner, exception.Message, "Feather recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? ShowBackupPassword(Window owner, string title, string description, bool confirmPassword)
    {
        var window = new Window
        {
            Owner = owner,
            Title = title,
            Width = 460,
            Height = confirmPassword ? 330 : 275,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(12, 18, 25)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(145, 160, 178)),
            Margin = new Thickness(0, 0, 0, 16)
        });

        StackPanel firstPanel = PasswordField("Backup password", out PasswordBox first);
        root.Children.Add(firstPanel);

        PasswordBox? second = null;
        if (confirmPassword)
            root.Children.Add(PasswordField("Confirm backup password", out second));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var cancel = Button("Cancel", false);
        var submit = Button(confirmPassword ? "Continue" : "Unlock", true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(submit);
        root.Children.Add(buttons);

        string? result = null;
        cancel.Click += (_, _) => window.DialogResult = false;
        submit.Click += (_, _) =>
        {
            string value = first.Password;
            if (value.Length < 12)
            {
                MessageBox.Show(window, "Use at least 12 characters for the recovery backup password.", "Feather recovery", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (confirmPassword && second is not null && !string.Equals(value, second.Password, StringComparison.Ordinal))
            {
                MessageBox.Show(window, "The backup passwords do not match.", "Feather recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            result = value;
            first.Clear();
            second?.Clear();
            window.DialogResult = true;
        };

        window.Content = root;
        first.Focus();
        return window.ShowDialog() == true ? result : null;
    }

    private static StackPanel Field(string label, out TextBox input)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(Label(label));
        input = new TextBox
        {
            Height = 38,
            Padding = new Thickness(10, 7, 10, 7),
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(8, 13, 19)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 63, 82))
        };
        panel.Children.Add(input);
        return panel;
    }

    private static StackPanel PasswordField(string label, out PasswordBox input)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(Label(label));
        input = new PasswordBox
        {
            Height = 38,
            Padding = new Thickness(10, 7, 10, 7),
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(8, 13, 19)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 63, 82))
        };
        panel.Children.Add(input);
        return panel;
    }

    private static TextBlock Label(string text) =>
        new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 194, 210)),
            Margin = new Thickness(0, 0, 0, 6)
        };

    private static Button Button(string text, bool primary) =>
        new()
        {
            Content = text,
            MinWidth = 96,
            Height = 34,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 4, 12, 4),
            Background = new SolidColorBrush(primary ? Color.FromRgb(53, 153, 235) : Color.FromRgb(22, 32, 43)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(85, 179, 255) : Color.FromRgb(52, 72, 92))
        };
}

internal sealed record AccountCredentials(string Email, string Password);

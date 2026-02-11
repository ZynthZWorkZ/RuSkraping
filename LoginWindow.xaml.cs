using System;
using System.Windows;
using System.Windows.Input;
using RuSkraping.Services;

namespace RuSkraping;

/// <summary>
/// Interaction logic for LoginWindow.xaml
/// </summary>
public partial class LoginWindow : Window
{
    public string Username { get; private set; } = string.Empty;
    public string Password { get; private set; } = string.Empty;
    public bool LoginSuccessful { get; private set; } = false;
    public List<Models.CookieData>? Cookies { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        
        // Try to load saved credentials
        var credentials = CredentialManager.LoadCredentials();
        if (credentials.HasValue)
        {
            UsernameTextBox.Text = credentials.Value.Username;
            // Don't auto-fill password for security
        }
        
        // Set focus to username if empty, otherwise password
        if (string.IsNullOrEmpty(UsernameTextBox.Text))
        {
            UsernameTextBox.Focus();
        }
        else
        {
            PasswordBox.Focus();
        }
        
        // Handle Enter key
        UsernameTextBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                PasswordBox.Focus();
            }
        };
        
        PasswordBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                LoginButton_Click(LoginButton, new RoutedEventArgs());
            }
        };
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        Username = UsernameTextBox.Text.Trim();
        Password = PasswordBox.Password;

        if (string.IsNullOrEmpty(Username))
        {
            MessageBox.Show("Please enter your username.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            UsernameTextBox.Focus();
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            MessageBox.Show("Please enter your password.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            PasswordBox.Focus();
            return;
        }

        // Disable UI during login
        LoginButton.IsEnabled = false;
        ManualLoginButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        UsernameTextBox.IsEnabled = false;
        PasswordBox.IsEnabled = false;
        StatusText.Text = "Logging in... This may take a moment...";
        StatusText.Visibility = Visibility.Visible;

        try
        {
            // Perform login
            var loginService = new RuTrackerLoginService();
            bool showBrowser = ShowBrowserCheckBox.IsChecked ?? false;
            var result = await loginService.LoginAsync(Username, Password, showBrowser: showBrowser);

            if (result.Success && result.Cookies.Count > 0)
            {
        // Save credentials securely
        CredentialManager.SaveCredentials(Username, Password);
                
                // Save cookies
                CookieStorage.SaveCookies(result.Cookies);
                Cookies = result.Cookies;
        
        LoginSuccessful = true;
                StatusText.Text = "Login successful!";
                
                await Task.Delay(500); // Brief delay to show success message
                
        DialogResult = true;
        Close();
            }
            else
            {
                StatusText.Text = "Login failed. Please check your credentials.";
                StatusText.Visibility = Visibility.Visible;
                MessageBox.Show("Login failed. Please check your username and password.", 
                    "Login Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Re-enable UI
                LoginButton.IsEnabled = true;
                ManualLoginButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
                UsernameTextBox.IsEnabled = true;
                PasswordBox.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "An error occurred during login.";
            StatusText.Visibility = Visibility.Visible;
            MessageBox.Show($"An error occurred during login: {ex.Message}", 
                "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // Re-enable UI
            LoginButton.IsEnabled = true;
            ManualLoginButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            UsernameTextBox.IsEnabled = true;
            PasswordBox.IsEnabled = true;
        }
    }

    private async void ManualLoginButton_Click(object sender, RoutedEventArgs e)
    {
        // Disable UI during manual login
        LoginButton.IsEnabled = false;
        ManualLoginButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        UsernameTextBox.IsEnabled = false;
        PasswordBox.IsEnabled = false;
        ShowBrowserCheckBox.IsEnabled = false;
        StatusText.Text = "Opening browser... Please login manually and click OK when done.";
        StatusText.Visibility = Visibility.Visible;

        try
        {
            // Perform manual login
            var loginService = new RuTrackerLoginService();
            var result = await loginService.ManualLoginAsync();

            if (result.Success && result.Cookies.Count > 0)
            {
                // Save cookies (no credentials since user logged in manually)
                CookieStorage.SaveCookies(result.Cookies);
                Cookies = result.Cookies;
        
                LoginSuccessful = true;
                StatusText.Text = "Manual login successful!";
                
                await Task.Delay(500); // Brief delay to show success message
                
                DialogResult = true;
                Close();
            }
            else
            {
                StatusText.Text = "Manual login was cancelled or failed.";
                StatusText.Visibility = Visibility.Visible;
                MessageBox.Show("Manual login was cancelled or no cookies were obtained.", 
                    "Login Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Re-enable UI
                LoginButton.IsEnabled = true;
                ManualLoginButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
                UsernameTextBox.IsEnabled = true;
                PasswordBox.IsEnabled = true;
                ShowBrowserCheckBox.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "An error occurred during manual login.";
            StatusText.Visibility = Visibility.Visible;
            MessageBox.Show($"An error occurred during manual login: {ex.Message}", 
                "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // Re-enable UI
            LoginButton.IsEnabled = true;
            ManualLoginButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            UsernameTextBox.IsEnabled = true;
            PasswordBox.IsEnabled = true;
            ShowBrowserCheckBox.IsEnabled = true;
        }
    }
}


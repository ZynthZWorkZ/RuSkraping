using System.Configuration;
using System.Data;
using System.Windows;

namespace RuSkraping;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check if we have valid cookies
        if (!CookieStorage.HasValidCookies())
        {
            // Show login window
            var loginWindow = new LoginWindow();
            var loginResult = loginWindow.ShowDialog();
            
            if (loginResult == true && loginWindow.LoginSuccessful)
            {
                // Login successful, wait a moment for ChromeDriver to fully close
                System.Threading.Thread.Sleep(1000);
                
                // Login successful, continue to main window
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
            else
            {
                // User cancelled or login failed, exit application
                Shutdown();
                return;
            }
        }
        else
        {
            // We have valid cookies, go straight to main window
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}


using System;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;

namespace RuSkraping;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Log application start
        ErrorLogger.LogMessage("═══════════════════════════════════════════════════════", "INFO");
        ErrorLogger.LogMessage("Application Starting", "INFO");
        ErrorLogger.LogMessage($"Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}", "INFO");
        ErrorLogger.LogMessage($"OS: {Environment.OSVersion}", "INFO");
        ErrorLogger.LogMessage($".NET: {Environment.Version}", "INFO");
        ErrorLogger.LogMessage("═══════════════════════════════════════════════════════", "INFO");

        // Setup global exception handlers
        SetupExceptionHandlers();

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

    private void SetupExceptionHandlers()
    {
        // Handle UI thread exceptions
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        // Handle non-UI thread exceptions
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Handle task exceptions
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        ErrorLogger.LogMessage("Exception handlers registered", "INFO");
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLogger.LogException(e.Exception, "UI Thread Exception");
        
        MessageBox.Show(
            $"An error occurred:\n\n{e.Exception.Message}\n\n" +
            $"Full details have been logged to:\n{ErrorLogger.GetLogFilePath()}\n\n" +
            $"Click OK to continue or close the application.",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Mark as handled to prevent crash
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            ErrorLogger.LogException(ex, "Non-UI Thread Exception (Critical)");
            
            MessageBox.Show(
                $"A critical error occurred:\n\n{ex.Message}\n\n" +
                $"Full details have been logged to:\n{ErrorLogger.GetLogFilePath()}\n\n" +
                $"The application will now close.",
                "Critical Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        // Check if this is an expected network exception from the torrent engine
        // (SocketException, IOException, ObjectDisposedException are normal during peer disconnect)
        bool isExpectedNetworkError = IsExpectedNetworkException(e.Exception);

        if (isExpectedNetworkError)
        {
            // Log at WARN level — these are expected and not actionable
            ErrorLogger.LogMessage($"[TaskScheduler] Suppressed expected network exception: {e.Exception.InnerException?.Message ?? e.Exception.Message}", "WARN");
        }
        else
        {
            // Log at ERROR level — these are unexpected and should be investigated
            ErrorLogger.LogException(e.Exception, "Unobserved Task Exception");
        }
        
        // Mark as observed to prevent app crash
        e.SetObserved();
    }

    /// <summary>
    /// Checks if an AggregateException contains only expected network exceptions
    /// (SocketException, IOException, ObjectDisposedException, OperationCanceledException).
    /// These are normal during peer disconnection/shutdown.
    /// </summary>
    private static bool IsExpectedNetworkException(AggregateException ex)
    {
        foreach (var inner in ex.InnerExceptions)
        {
            var actual = inner;
            // Unwrap nested AggregateExceptions
            while (actual is AggregateException agg && agg.InnerException != null)
                actual = agg.InnerException;

            if (actual is System.Net.Sockets.SocketException) continue;
            if (actual is System.IO.IOException) continue;
            if (actual is ObjectDisposedException) continue;
            if (actual is OperationCanceledException) continue;
            if (actual is System.Threading.Tasks.TaskCanceledException) continue;

            return false; // Found a non-network exception
        }
        return true;
    }
}


using System;
using System.IO;
using System.Text;

namespace RuSkraping;

/// <summary>
/// Centralized error logging system
/// </summary>
public static class ErrorLogger
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RuSkraping",
        "errors.log"
    );

    private static readonly object _lockObject = new();

    static ErrorLogger()
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(LogFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Clear old log if it's too large (> 10MB)
        try
        {
            if (File.Exists(LogFilePath))
            {
                var fileInfo = new FileInfo(LogFilePath);
                if (fileInfo.Length > 10 * 1024 * 1024) // 10MB
                {
                    File.Delete(LogFilePath);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Log an exception with full details
    /// </summary>
    public static void LogException(Exception ex, string context = "")
    {
        try
        {
            lock (_lockObject)
            {
                var sb = new StringBuilder();
                sb.AppendLine("═══════════════════════════════════════════════════════");
                sb.AppendLine($"ERROR LOGGED: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine("═══════════════════════════════════════════════════════");
                
                if (!string.IsNullOrEmpty(context))
                {
                    sb.AppendLine($"CONTEXT: {context}");
                    sb.AppendLine();
                }

                sb.AppendLine($"EXCEPTION TYPE: {ex.GetType().FullName}");
                sb.AppendLine($"MESSAGE: {ex.Message}");
                sb.AppendLine();

                if (!string.IsNullOrEmpty(ex.Source))
                {
                    sb.AppendLine($"SOURCE: {ex.Source}");
                }

                if (ex.TargetSite != null)
                {
                    sb.AppendLine($"TARGET SITE: {ex.TargetSite}");
                }

                sb.AppendLine();
                sb.AppendLine("STACK TRACE:");
                sb.AppendLine(ex.StackTrace ?? "(No stack trace available)");

                // Log inner exceptions
                var innerEx = ex.InnerException;
                int innerCount = 1;
                while (innerEx != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"───────── INNER EXCEPTION #{innerCount} ─────────");
                    sb.AppendLine($"TYPE: {innerEx.GetType().FullName}");
                    sb.AppendLine($"MESSAGE: {innerEx.Message}");
                    sb.AppendLine("STACK TRACE:");
                    sb.AppendLine(innerEx.StackTrace ?? "(No stack trace available)");
                    
                    innerEx = innerEx.InnerException;
                    innerCount++;
                }

                // Log additional data if available
                if (ex.Data.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("ADDITIONAL DATA:");
                    foreach (var key in ex.Data.Keys)
                    {
                        sb.AppendLine($"  {key}: {ex.Data[key]}");
                    }
                }

                sb.AppendLine("═══════════════════════════════════════════════════════");
                sb.AppendLine();

                File.AppendAllText(LogFilePath, sb.ToString());
            }
        }
        catch (Exception logEx)
        {
            // If logging fails, try to write to a backup location
            try
            {
                var backupPath = Path.Combine(Path.GetTempPath(), "RuSkraping_errors.log");
                File.AppendAllText(backupPath, $"LOGGING ERROR: {logEx.Message}\n");
                File.AppendAllText(backupPath, $"ORIGINAL ERROR: {ex.Message}\n\n");
            }
            catch
            {
                // If even that fails, silently continue
            }
        }
    }

    /// <summary>
    /// Log a general message
    /// </summary>
    public static void LogMessage(string message, string level = "INFO")
    {
        try
        {
            lock (_lockObject)
            {
                var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}\n";
                File.AppendAllText(LogFilePath, logLine);
            }
        }
        catch { }
    }

    /// <summary>
    /// Get the log file path for display
    /// </summary>
    public static string GetLogFilePath()
    {
        return LogFilePath;
    }

    /// <summary>
    /// Open the log file in default text editor
    /// </summary>
    public static void OpenLogFile()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = LogFilePath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not open log file:\n{ex.Message}", 
                "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Clear the log file
    /// </summary>
    public static void ClearLog()
    {
        try
        {
            lock (_lockObject)
            {
                if (File.Exists(LogFilePath))
                {
                    File.Delete(LogFilePath);
                }
                LogMessage("Log cleared", "INFO");
            }
        }
        catch { }
    }
}

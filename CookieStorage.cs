using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;
using RuSkraping.Models;

namespace RuSkraping;

/// <summary>
/// Manages cookie storage and retrieval
/// </summary>
public static class CookieStorage
{
    private static readonly string CookiesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RuSkraping",
        "cookies.json"
    );

    /// <summary>
    /// Saves cookies to file
    /// </summary>
    public static void SaveCookies(List<CookieData> cookies)
    {
        try
        {
            var directory = Path.GetDirectoryName(CookiesFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(cookies, options);
            File.WriteAllText(CookiesFilePath, json);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to save cookies: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Loads cookies from file
    /// </summary>
    public static List<CookieData>? LoadCookies()
    {
        try
        {
            if (!File.Exists(CookiesFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(CookiesFilePath);
            var cookies = JsonSerializer.Deserialize<List<CookieData>>(json);
            
            // Check if cookies are expired
            if (cookies != null)
            {
                var now = DateTime.UtcNow;
                var validCookies = cookies.Where(c => 
                    c.Session || 
                    (c.Expires.HasValue && c.Expires.Value > now)
                ).ToList();
                
                if (validCookies.Count != cookies.Count)
                {
                    // Some cookies expired, save the valid ones
                    SaveCookies(validCookies);
                    return validCookies.Count > 0 ? validCookies : null;
                }
            }
            
            return cookies;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if valid cookies exist
    /// </summary>
    public static bool HasValidCookies()
    {
        var cookies = LoadCookies();
        return cookies != null && cookies.Count > 0;
    }

    /// <summary>
    /// Deletes saved cookies
    /// </summary>
    public static void DeleteCookies()
    {
        try
        {
            if (File.Exists(CookiesFilePath))
            {
                File.Delete(CookiesFilePath);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to delete cookies: {ex.Message}", ex);
        }
    }
}


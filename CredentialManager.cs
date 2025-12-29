using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RuSkraping;

/// <summary>
/// Manages secure credential storage using Windows Data Protection API (DPAPI)
/// </summary>
public static class CredentialManager
{
    private static readonly string CredentialsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RuSkraping",
        "credentials.dat"
    );

    /// <summary>
    /// Saves credentials securely using DPAPI encryption
    /// </summary>
    public static void SaveCredentials(string username, string password)
    {
        try
        {
            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(CredentialsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create credentials object
            var credentials = new
            {
                Username = username,
                Password = password
            };

            // Serialize to JSON
            string json = JsonSerializer.Serialize(credentials);

            // Encrypt using DPAPI (Windows Data Protection API)
            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = ProtectedData.Protect(
                data,
                null, // Optional entropy (additional random data)
                DataProtectionScope.CurrentUser // Only current user can decrypt
            );

            // Save to file
            File.WriteAllBytes(CredentialsFilePath, encrypted);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to save credentials: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Loads credentials securely using DPAPI decryption
    /// </summary>
    public static (string Username, string Password)? LoadCredentials()
    {
        try
        {
            if (!File.Exists(CredentialsFilePath))
            {
                return null;
            }

            // Read encrypted data
            byte[] encrypted = File.ReadAllBytes(CredentialsFilePath);

            // Decrypt using DPAPI
            byte[] decrypted = ProtectedData.Unprotect(
                encrypted,
                null, // Optional entropy (must match what was used for encryption)
                DataProtectionScope.CurrentUser
            );

            // Deserialize from JSON
            string json = Encoding.UTF8.GetString(decrypted);
            var credentials = JsonSerializer.Deserialize<CredentialsData>(json);

            if (credentials == null || string.IsNullOrEmpty(credentials.Username) || string.IsNullOrEmpty(credentials.Password))
            {
                return null;
            }

            return (credentials.Username, credentials.Password);
        }
        catch (Exception)
        {
            // If decryption fails (e.g., file corrupted or user changed), return null
            return null;
        }
    }

    /// <summary>
    /// Deletes saved credentials
    /// </summary>
    public static void DeleteCredentials()
    {
        try
        {
            if (File.Exists(CredentialsFilePath))
            {
                File.Delete(CredentialsFilePath);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to delete credentials: {ex.Message}", ex);
        }
    }

    private class CredentialsData
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}


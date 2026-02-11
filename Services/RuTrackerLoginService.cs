using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using RuSkraping.Models;

namespace RuSkraping.Services;

public class RuTrackerLoginService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://rutracker.org";
    private const string LoginUrl = "https://rutracker.org/forum/login.php";

    public RuTrackerLoginService()
    {
        // Register code page encodings (needed for Windows-1251)
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:146.0) Gecko/20100101 Firefox/146.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", 
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br, zstd");
    }

    /// <summary>
    /// Gets cookies from the website using Selenium (handles Cloudflare challenge)
    /// Returns the driver so it can be reused for importing cookies later
    /// </summary>
    /// <param name="showBrowser">If true, shows the browser window during login (false = headless mode)</param>
    public async Task<(List<CookieData> cookies, ChromeDriver driver)> GetCookiesBeforeLoginAsync(bool showBrowser = false)
    {
        // Configure ChromeDriverService similar to SRXMDL pattern
        var chromeDriverService = ChromeDriverService.CreateDefaultService();
        chromeDriverService.HideCommandPromptWindow = true;
        chromeDriverService.SuppressInitialDiagnosticInformation = true;
        
        var chromeOptions = new ChromeOptions();
        
        // Try to find Chrome in common locations
        string chromePath = FindChromeExecutable();
        if (!string.IsNullOrEmpty(chromePath))
        {
            chromeOptions.BinaryLocation = chromePath;
        }
        
        // Use headless mode to avoid conflicts with existing Chrome instances (unless user wants to see the browser)
        if (!showBrowser)
        {
        chromeOptions.AddArgument("--headless=new");
        chromeOptions.AddArgument("--disable-gpu");
        }
        chromeOptions.AddArgument("--no-sandbox");
        chromeOptions.AddArgument("--disable-dev-shm-usage");
        chromeOptions.AddArgument("--disable-blink-features=AutomationControlled");
        chromeOptions.AddArgument("--window-size=1920,1080");
        chromeOptions.AddArgument("--disable-extensions");
        chromeOptions.AddArgument("--disable-software-rasterizer");
        chromeOptions.AddArgument("--disable-notifications");
        chromeOptions.AddArgument("--disable-popup-blocking");
        chromeOptions.AddArgument("--disable-infobars");
        chromeOptions.AddArgument("--disable-logging");
        chromeOptions.AddArgument("--log-level=3");
        chromeOptions.AddArgument("--silent");
        // Use a different port to avoid conflicts with MainWindow's ChromeDriver
        chromeOptions.AddArgument("--remote-debugging-port=9223");
        chromeOptions.AddExcludedArgument("enable-automation");
        chromeOptions.AddAdditionalOption("useAutomationExtension", false);
        chromeOptions.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:146.0) Gecko/20100101 Firefox/146.0");

        ChromeDriver? driver = null;
        
        try
        {
            driver = new ChromeDriver(chromeDriverService, chromeOptions);
            driver.Navigate().GoToUrl($"{BaseUrl}/forum/login.php?redirect=tracker.php?nm=");
            
            // Wait for page to load and Cloudflare challenge to complete
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
            wait.Until(d => 
            {
                try
                {
                    return d.Url.Contains("rutracker.org") && 
                           ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete";
                }
                catch
                {
                    return false;
                }
            });
            
            await Task.Delay(3000);
            
            var cookies = new List<CookieData>();
            var seleniumCookies = driver.Manage().Cookies.AllCookies;
            
            foreach (var cookie in seleniumCookies)
            {
                var isHttpOnly = cookie.Name == "bb_session";
                
                cookies.Add(new CookieData
                {
                    Name = cookie.Name,
                    Value = cookie.Value,
                    Domain = cookie.Domain,
                    Path = cookie.Path ?? "/",
                    Secure = cookie.Secure,
                    HttpOnly = isHttpOnly,
                    Expires = cookie.Expiry,
                    Session = !cookie.Expiry.HasValue
                });
            }
            
            return (cookies, driver);
        }
        catch (Exception ex)
        {
            // Try with minimal options for maximum compatibility
            try
            {
                var minimalService = ChromeDriverService.CreateDefaultService();
                minimalService.HideCommandPromptWindow = true;
                
                var minimalOptions = new ChromeOptions();
                string minimalChromePath = FindChromeExecutable();
                if (!string.IsNullOrEmpty(minimalChromePath))
                {
                    minimalOptions.BinaryLocation = minimalChromePath;
                }
                if (!showBrowser)
                {
                minimalOptions.AddArgument("--headless=new");
                }
                minimalOptions.AddArgument("--no-sandbox");
                minimalOptions.AddArgument("--disable-dev-shm-usage");
                minimalOptions.AddArgument("--remote-debugging-port=9223");
                
                driver?.Dispose();
                driver = new ChromeDriver(minimalService, minimalOptions);
                driver.Navigate().GoToUrl($"{BaseUrl}/forum/login.php?redirect=tracker.php?nm=");
                
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                wait.Until(d => 
                {
                    try
                    {
                        return d.Url.Contains("rutracker.org") && 
                               ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete";
                    }
                    catch
                    {
                        return false;
                    }
                });
                
                await Task.Delay(3000);
                
                var cookies = new List<CookieData>();
                var seleniumCookies = driver.Manage().Cookies.AllCookies;
                
                foreach (var cookie in seleniumCookies)
                {
                    var isHttpOnly = cookie.Name == "bb_session";
                    
                    cookies.Add(new CookieData
                    {
                        Name = cookie.Name,
                        Value = cookie.Value,
                        Domain = cookie.Domain,
                        Path = cookie.Path ?? "/",
                        Secure = cookie.Secure,
                        HttpOnly = isHttpOnly,
                        Expires = cookie.Expiry,
                        Session = !cookie.Expiry.HasValue
                    });
                }
                
                return (cookies, driver);
            }
            catch (Exception ex2)
            {
                throw new Exception($"Failed to initialize Chrome.\n" +
                    $"Initial attempt error: {ex.Message}\n" +
                    $"Retry attempt error: {ex2.Message}\n" +
                    $"Please ensure Chrome is installed and up to date.", ex2);
            }
        }
    }

    private string FindChromeExecutable()
    {
        // Common Chrome installation paths on Windows
        var possiblePaths = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
            Environment.ExpandEnvironmentVariables(@"%PROGRAMFILES%\Google\Chrome\Application\chrome.exe"),
            Environment.ExpandEnvironmentVariables(@"%PROGRAMFILES(X86)%\Google\Chrome\Application\chrome.exe")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
    
    /// <summary>
    /// Imports cookies into Chrome browser and verifies auto-login
    /// </summary>
    public async Task<bool> ImportCookiesAndVerifyLoginAsync(ChromeDriver driver, List<CookieData> cookies, string redirect = "tracker.php?nm=")
    {
        try
        {
            // Navigate to the site first (required for setting cookies)
            driver.Navigate().GoToUrl($"{BaseUrl}/forum/");
            await Task.Delay(1000);
            
            // Delete all existing cookies first
            driver.Manage().Cookies.DeleteAllCookies();
            
            // Import cookies from our list
            int importedCount = 0;
            foreach (var cookie in cookies)
            {
                try
                {
                    string domain = cookie.Domain ?? "rutracker.org";
                    if (domain.StartsWith("."))
                    {
                        domain = domain.Substring(1);
                    }
                    if (domain.StartsWith("http://") || domain.StartsWith("https://"))
                    {
                        var uri = new Uri(domain);
                        domain = uri.Host;
                    }
                    
                    var seleniumCookie = new OpenQA.Selenium.Cookie(
                        cookie.Name,
                        cookie.Value,
                        domain,
                        cookie.Path ?? "/",
                        cookie.Expires
                    );
                    
                    driver.Manage().Cookies.AddCookie(seleniumCookie);
                    importedCount++;
                }
                catch (Exception ex)
                {
                    // Log warning but continue
                    System.Diagnostics.Debug.WriteLine($"Warning: Could not import cookie {cookie.Name}: {ex.Message}");
                }
            }
            
            // Navigate to the redirect URL to verify auto-login
            var verifyUrl = redirect.StartsWith("http") ? redirect : $"{BaseUrl}/forum/{redirect}";
            driver.Navigate().GoToUrl(verifyUrl);
            
            // Wait for page to load
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
            wait.Until(d => 
            {
                try
                {
                    return ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete";
                }
                catch
                {
                    return false;
                }
            });
            
            await Task.Delay(2000);
            
            // Check if we're logged in by looking for user-specific elements
            try
            {
                var currentUrl = driver.Url;
                
                // Check if we're not on login page (indicates success)
                if (!currentUrl.Contains("login.php"))
                {
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Could not verify login status: {ex.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error importing cookies: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens browser for manual login and extracts cookies after user completes login
    /// </summary>
    /// <returns>LoginResult with extracted cookies</returns>
    public async Task<LoginResult> ManualLoginAsync()
    {
        ChromeDriver? driver = null;
        
        try
        {
            // Configure ChromeDriverService
            var chromeDriverService = ChromeDriverService.CreateDefaultService();
            chromeDriverService.HideCommandPromptWindow = true;
            chromeDriverService.SuppressInitialDiagnosticInformation = true;
            
            var chromeOptions = new ChromeOptions();
            
            // Try to find Chrome in common locations
            string chromePath = FindChromeExecutable();
            if (!string.IsNullOrEmpty(chromePath))
            {
                chromeOptions.BinaryLocation = chromePath;
            }
            
            // Always show browser for manual login (no headless mode)
            chromeOptions.AddArgument("--no-sandbox");
            chromeOptions.AddArgument("--disable-dev-shm-usage");
            chromeOptions.AddArgument("--disable-blink-features=AutomationControlled");
            chromeOptions.AddArgument("--window-size=1280,900");
            chromeOptions.AddArgument("--disable-extensions");
            chromeOptions.AddArgument("--disable-infobars");
            chromeOptions.AddArgument("--remote-debugging-port=9224");
            chromeOptions.AddExcludedArgument("enable-automation");
            chromeOptions.AddAdditionalOption("useAutomationExtension", false);
            chromeOptions.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:146.0) Gecko/20100101 Firefox/146.0");

            driver = new ChromeDriver(chromeDriverService, chromeOptions);
            
            // Navigate to login page
            driver.Navigate().GoToUrl($"{BaseUrl}/forum/login.php");
            
            // Wait for page to load
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
            wait.Until(d => 
            {
                try
                {
                    return ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete";
                }
                catch
                {
                    return false;
                }
            });
            
            await Task.Delay(2000);
            
            // Show message box to user - they need to login manually
            var result = System.Windows.MessageBox.Show(
                "Please login to RuTracker in the browser window.\n\n" +
                "After successfully logging in, click OK to extract your cookies.\n\n" +
                "Click Cancel to abort the manual login process.",
                "Manual Login",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Information);
            
            if (result == System.Windows.MessageBoxResult.Cancel)
            {
                return new LoginResult
                {
                    Success = false,
                    Cookies = new List<CookieData>(),
                    StatusCode = System.Net.HttpStatusCode.RequestTimeout
                };
            }
            
            // Extract cookies after user confirms they've logged in
            var cookies = new List<CookieData>();
            var seleniumCookies = driver.Manage().Cookies.AllCookies;
            
            foreach (var cookie in seleniumCookies)
            {
                var isHttpOnly = cookie.Name == "bb_session";
                
                cookies.Add(new CookieData
                {
                    Name = cookie.Name,
                    Value = cookie.Value,
                    Domain = cookie.Domain,
                    Path = cookie.Path ?? "/",
                    Secure = cookie.Secure,
                    HttpOnly = isHttpOnly,
                    Expires = cookie.Expiry,
                    Session = !cookie.Expiry.HasValue
                });
            }
            
            // Close browser
            try
            {
                driver.Quit();
                driver.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Error closing browser: {ex.Message}");
            }
            
            return new LoginResult
            {
                Success = cookies.Count > 0,
                Cookies = cookies,
                StatusCode = cookies.Count > 0 ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.Unauthorized
            };
        }
        catch (Exception ex)
        {
            // Clean up driver if still open
            try
            {
                driver?.Quit();
                driver?.Dispose();
            }
            catch { }
            
            throw new Exception($"Failed to perform manual login: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Performs login and returns updated cookies
    /// </summary>
    /// <param name="username">RuTracker username</param>
    /// <param name="password">RuTracker password</param>
    /// <param name="redirect">Redirect URL after login</param>
    /// <param name="showBrowser">If true, shows the browser window during login (false = headless mode)</param>
    public async Task<LoginResult> LoginAsync(string username, string password, string redirect = "tracker.php?nm=", bool showBrowser = false)
    {
        // Step 1: Get cookies before login using Selenium
        var (beforeCookies, chromeDriver) = await GetCookiesBeforeLoginAsync(showBrowser);
        
        // Step 2: Prepare login request
        var cookieContainer = new System.Net.CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = cookieContainer };
        var loginClient = new HttpClient(handler);
        loginClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:146.0) Gecko/20100101 Firefox/146.0");
        loginClient.DefaultRequestHeaders.Add("Accept", 
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        loginClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        loginClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br, zstd");
        loginClient.DefaultRequestHeaders.Add("Referer", 
            $"{BaseUrl}/forum/login.php?redirect={Uri.EscapeDataString(redirect)}");
        loginClient.DefaultRequestHeaders.Add("Origin", BaseUrl);
        loginClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        
        // Add cookies to container
        foreach (var cookie in beforeCookies)
        {
            try
            {
                var netCookie = new System.Net.Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
                {
                    Secure = cookie.Secure,
                    HttpOnly = cookie.HttpOnly
                };
                
                if (cookie.Expires.HasValue)
                {
                    netCookie.Expires = cookie.Expires.Value;
                }
                
                cookieContainer.Add(new Uri(BaseUrl), netCookie);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Could not add cookie {cookie.Name}: {ex.Message}");
            }
        }
        
        // Step 3: Build login POST data
        // The login button value is Windows-1251 encoded: "Вход"
        byte[] loginButtonBytes = { 0xC2, 0xF5, 0xEE, 0xE4 };
        string loginButtonValue = Encoding.GetEncoding(1251).GetString(loginButtonBytes);
        
        var formData = new List<KeyValuePair<string, string>>
        {
            new("redirect", redirect),
            new("login_username", username),
            new("login_password", password),
            new("login", loginButtonValue)
        };
        
        var formContent = new FormUrlEncodedContent(formData);
        
        // Step 4: Send login request
        var loginResponse = await loginClient.PostAsync(LoginUrl, formContent);
        
        // Step 5: Extract cookies from response
        var afterCookies = new List<CookieData>();
        
        // Get cookies from response
        if (loginResponse.Headers.Contains("Set-Cookie"))
        {
            var setCookieHeaders = loginResponse.Headers.GetValues("Set-Cookie");
            foreach (var setCookieHeader in setCookieHeaders)
            {
                var cookie = ParseSetCookieHeader(setCookieHeader);
                if (cookie != null)
                {
                    afterCookies.Add(cookie);
                }
            }
        }
        
        // Also get cookies from the cookie container
        var responseUri = loginResponse.RequestMessage?.RequestUri ?? new Uri(BaseUrl);
        var containerCookies = cookieContainer.GetCookies(responseUri);
        
        foreach (System.Net.Cookie netCookie in containerCookies)
        {
            var existingCookie = afterCookies.FirstOrDefault(c => c.Name == netCookie.Name);
            if (existingCookie == null)
            {
                afterCookies.Add(new CookieData
                {
                    Name = netCookie.Name,
                    Value = netCookie.Value,
                    Domain = netCookie.Domain,
                    Path = netCookie.Path,
                    Secure = netCookie.Secure,
                    HttpOnly = netCookie.HttpOnly,
                    Expires = netCookie.Expires == DateTime.MinValue ? null : netCookie.Expires,
                    Session = netCookie.Expires == DateTime.MinValue
                });
            }
            else
            {
                existingCookie.Value = netCookie.Value;
            }
        }
        
        // Step 6: Follow redirect if needed and get final cookies
        Uri? redirectLocation = null;
        if (loginResponse.StatusCode == System.Net.HttpStatusCode.Found || 
            loginResponse.StatusCode == System.Net.HttpStatusCode.Moved ||
            loginResponse.StatusCode == System.Net.HttpStatusCode.SeeOther ||
            (int)loginResponse.StatusCode >= 300 && (int)loginResponse.StatusCode < 400)
        {
            redirectLocation = loginResponse.Headers.Location;
            if (redirectLocation != null)
            {
                if (!redirectLocation.IsAbsoluteUri)
                {
                    redirectLocation = new Uri(new Uri(BaseUrl), redirectLocation);
                }
                
                try
                {
                    var redirectResponse = await loginClient.GetAsync(redirectLocation);
                    
                    // Get final cookies after redirect
                    var finalCookies = cookieContainer.GetCookies(redirectLocation);
                    foreach (System.Net.Cookie netCookie in finalCookies)
                    {
                        var existing = afterCookies.FirstOrDefault(c => c.Name == netCookie.Name);
                        if (existing != null)
                        {
                            existing.Value = netCookie.Value;
                        }
                        else
                        {
                            afterCookies.Add(new CookieData
                            {
                                Name = netCookie.Name,
                                Value = netCookie.Value,
                                Domain = netCookie.Domain,
                                Path = netCookie.Path,
                                Secure = netCookie.Secure,
                                HttpOnly = netCookie.HttpOnly,
                                Expires = netCookie.Expires == DateTime.MinValue ? null : netCookie.Expires,
                                Session = netCookie.Expires == DateTime.MinValue
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Error following redirect: {ex.Message}");
                }
            }
        }
        
        // Also check cookies from the cookie container for the base URL
        try
        {
            var baseUriCookies = cookieContainer.GetCookies(new Uri(BaseUrl));
            foreach (System.Net.Cookie netCookie in baseUriCookies)
            {
                var existing = afterCookies.FirstOrDefault(c => c.Name == netCookie.Name);
                if (existing != null)
                {
                    if (existing.Value != netCookie.Value)
                    {
                        existing.Value = netCookie.Value;
                    }
                }
                else
                {
                    afterCookies.Add(new CookieData
                    {
                        Name = netCookie.Name,
                        Value = netCookie.Value,
                        Domain = netCookie.Domain,
                        Path = netCookie.Path,
                        Secure = netCookie.Secure,
                        HttpOnly = netCookie.HttpOnly,
                        Expires = netCookie.Expires == DateTime.MinValue ? null : netCookie.Expires,
                        Session = netCookie.Expires == DateTime.MinValue
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warning: Error getting base URI cookies: {ex.Message}");
        }
        
        loginClient.Dispose();
        
        // Step 7: Import cookies back into Chrome and verify auto-login
        var loginVerified = await ImportCookiesAndVerifyLoginAsync(chromeDriver, afterCookies, redirect);
        
        // Close browser after login
        try
        {
            chromeDriver.Quit();
            chromeDriver.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warning: Error closing browser: {ex.Message}");
        }
        
        return new LoginResult
        {
            Success = loginResponse.IsSuccessStatusCode || 
                     loginResponse.StatusCode == System.Net.HttpStatusCode.Found,
            Cookies = afterCookies,
            StatusCode = loginResponse.StatusCode
        };
    }

    private CookieData? ParseSetCookieHeader(string setCookieHeader)
    {
        var parts = setCookieHeader.Split(';');
        if (parts.Length == 0) return null;
        
        var nameValue = parts[0].Split('=', 2);
        if (nameValue.Length != 2) return null;
        
        var cookie = new CookieData
        {
            Name = nameValue[0].Trim(),
            Value = nameValue[1].Trim(),
            Path = "/forum/",
            Domain = ".rutracker.org"
        };
        
        for (int i = 1; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (part.Equals("Secure", StringComparison.OrdinalIgnoreCase))
                cookie.Secure = true;
            else if (part.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase))
                cookie.HttpOnly = true;
            else if (part.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
                cookie.Path = part.Substring(5);
            else if (part.StartsWith("Domain=", StringComparison.OrdinalIgnoreCase))
                cookie.Domain = part.Substring(7);
            else if (part.StartsWith("Expires=", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.TryParse(part.Substring(8), out var expires))
                    cookie.Expires = expires;
            }
            else if (part.StartsWith("Max-Age=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(part.Substring(8), out var maxAge))
                    cookie.Expires = DateTime.UtcNow.AddSeconds(maxAge);
            }
        }
        
        return cookie;
    }
}

public class LoginResult
{
    public bool Success { get; set; }
    public List<CookieData> Cookies { get; set; } = new();
    public System.Net.HttpStatusCode StatusCode { get; set; }
}


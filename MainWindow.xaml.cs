using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Web;
using System.Windows.Input;

namespace RuSkraping;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private ObservableCollection<SearchResult> searchResults;
    private ChromeDriver driver;
    private string currentMagnetLink;
    private CancellationTokenSource searchCancellation;
    private bool isPaused;
    private ManualResetEvent pauseEvent;

    public MainWindow()
    {
        InitializeComponent();
        searchResults = new ObservableCollection<SearchResult>();
        ResultsListView.ItemsSource = searchResults;
        pauseEvent = new ManualResetEvent(true);
        
        try
        {
            InitializeChromeDriver();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize Chrome driver: {ex.Message}\n\nPlease make sure Chrome is installed and up to date.", 
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private void InitializeChromeDriver()
    {
        var options = new ChromeOptions();
        options.AddArgument("--headless=new");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--window-size=1920,1080");
        options.AddArgument("--disable-extensions");
        options.AddArgument("--disable-software-rasterizer");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-notifications");
        options.AddArgument("--disable-popup-blocking");
        options.AddArgument("--disable-infobars");
        options.AddArgument("--disable-logging");
        options.AddArgument("--log-level=3");
        options.AddArgument("--silent");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);

        var service = ChromeDriverService.CreateDefaultService();
        service.HideCommandPromptWindow = true;

        try
        {
            driver = new ChromeDriver(service, options);
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
        }
        catch (WebDriverException ex)
        {
            throw new Exception($"Chrome driver initialization failed: {ex.Message}", ex);
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            MessageBox.Show("Please enter a search query.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SearchButton.IsEnabled = false;
            PauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            SearchProgress.Visibility = Visibility.Visible;
            SearchLoadingSpinner.Visibility = Visibility.Visible;
            SearchProgressText.Text = "0%";
            SearchProgress.Value = 0;
            searchCancellation = new CancellationTokenSource();
            isPaused = false;
            pauseEvent.Set();

            await PerformSearch(searchCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Search stopped.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SearchButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            SearchProgress.Visibility = Visibility.Collapsed;
            SearchLoadingSpinner.Visibility = Visibility.Collapsed;
            SearchProgressText.Text = "";
            SearchProgress.Value = 0;
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!isPaused)
        {
            isPaused = true;
            pauseEvent.Reset();
            PauseButton.Content = "Resume";
            StatusText.Text = "Search paused...";
            SearchLoadingSpinner.Visibility = Visibility.Collapsed;
        }
        else
        {
            isPaused = false;
            pauseEvent.Set();
            PauseButton.Content = "Pause";
            StatusText.Text = "Resuming search...";
            SearchLoadingSpinner.Visibility = Visibility.Visible;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        searchCancellation?.Cancel();
        pauseEvent.Set(); // Ensure we're not stuck in a pause
        StatusText.Text = "Stopping search...";
        SearchLoadingSpinner.Visibility = Visibility.Collapsed;
    }

    private async Task PerformSearch(CancellationToken cancellationToken)
    {
        searchResults.Clear();
        StatusText.Text = "Searching...";
        GetMagnetButton.IsEnabled = false;
        OpenMagnetButton.IsEnabled = false;
        SearchProgress.Visibility = Visibility.Visible;
        SearchLoadingSpinner.Visibility = Visibility.Visible;
        SearchProgress.Value = 0;
        SearchProgressText.Text = "0%";

        try
        {
            // First visit the domain to set cookies
            driver.Navigate().GoToUrl("https://rutracker.org");

            // Load and set cookies if enabled
            if (UseCookiesCheckBox.IsChecked == true)
            {
                var cookies = LoadCookiesFromFile("rutackercookies.txt");
                foreach (var cookie in cookies)
                {
                    driver.Manage().Cookies.AddCookie(new Cookie(cookie.Key, cookie.Value));
                }
            }

            // Construct the target URL
            string baseUrl = "https://rutracker.org/forum/tracker.php?nm=";
            string encodedSearch = HttpUtility.UrlEncode(SearchTextBox.Text);
            string targetUrl = baseUrl + encodedSearch;

            // Visit the first page
            driver.Navigate().GoToUrl(targetUrl);

            // Get total number of pages
            int totalPages = GetTotalPages(driver);
            StatusText.Text = $"Found {totalPages} pages of results";

            // Process each page
            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pauseEvent.WaitOne(); // Wait if paused

                StatusText.Text = $"Processing page {pageNum} of {totalPages}...";
                double progress = (double)pageNum / totalPages * 100;
                await Dispatcher.InvokeAsync(() =>
                {
                    SearchProgress.Value = progress;
                    SearchProgressText.Text = $"{progress:F0}%";
                });

                // Extract titles from current page
                var pageResults = ExtractTitles(driver);
                foreach (var result in pageResults)
                {
                    await Dispatcher.InvokeAsync(() => searchResults.Add(result));
                }

                // If this isn't the last page, go to the next page
                if (pageNum < totalPages)
                {
                    if (!GoToPage(driver, pageNum + 1))
                    {
                        StatusText.Text = $"Failed to go to page {pageNum + 1}";
                        break;
                    }
                    await Task.Delay(1000, cancellationToken); // Small delay between pages
                }
            }

            StatusText.Text = $"Search complete. Found {searchResults.Count} results.";
            
            // Only fetch images if the checkbox is checked
            if (FetchImagesCheckBox.IsChecked == true)
            {
                StatusText.Text = $"Search complete. Found {searchResults.Count} results. Fetching images...";
                await FetchImagesForResults(cancellationToken);
            }

            StatusText.Text = $"Search complete. Found {searchResults.Count} results.";
        }
        finally
        {
            GetMagnetButton.IsEnabled = true;
            OpenMagnetButton.IsEnabled = true;
            SearchProgress.Value = 0;
            SearchProgressText.Text = "";
            SearchProgress.Visibility = Visibility.Collapsed;
            SearchLoadingSpinner.Visibility = Visibility.Collapsed;
        }
    }

    private async Task FetchImagesForResults(CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        foreach (var result in searchResults)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            tasks.Add(FetchImageForResult(result, cancellationToken));
            
            // Process in batches of 5 to avoid overwhelming the browser
            if (tasks.Count >= 5)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
                await Task.Delay(500, cancellationToken); // Small delay between batches
            }
        }
        
        // Process any remaining tasks
        if (tasks.Any())
        {
            await Task.WhenAll(tasks);
        }
    }

    private async Task FetchImageForResult(SearchResult result, CancellationToken cancellationToken)
    {
        try
        {
            // Navigate to the result's page
            driver.Navigate().GoToUrl(result.Link);
            
            // Wait for the page to load
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            
            // Try to find the image with the specified class
            var imageElement = wait.Until(d => 
            {
                try
                {
                    return d.FindElement(By.CssSelector("img.postImg.postImgAligned.img-right"));
                }
                catch
                {
                    return null;
                }
            });

            if (imageElement != null)
            {
                string imageUrl = imageElement.GetAttribute("src");
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        result.ImageUrl = imageUrl;
                    });
                }
            }
        }
        catch (Exception ex)
        {
            // Log the error but continue with other results
            Console.WriteLine($"Error fetching image for {result.Title}: {ex.Message}");
        }
    }

    private async void GetMagnetButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsListView.SelectedItem == null)
        {
            MessageBox.Show("Please select a result first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedResult = (SearchResult)ResultsListView.SelectedItem;
        StatusText.Text = "Getting magnet link...";
        GetMagnetButton.IsEnabled = false;
        OpenMagnetButton.IsEnabled = false;

        try
        {
            // Navigate to the topic page
            driver.Navigate().GoToUrl(selectedResult.Link);
            
            // Wait for and extract the magnet link
            currentMagnetLink = ExtractMagnetLink(driver);
            if (!string.IsNullOrEmpty(currentMagnetLink))
            {
                Clipboard.SetText(currentMagnetLink);
                StatusText.Text = "Magnet link copied to clipboard!";
                OpenMagnetButton.IsEnabled = true;
            }
            else
            {
                StatusText.Text = "Could not find magnet link.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error getting magnet link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Error getting magnet link.";
        }
        finally
        {
            GetMagnetButton.IsEnabled = true;
        }
    }

    private void OpenMagnetButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(currentMagnetLink))
        {
            MessageBox.Show("No magnet link available.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = currentMagnetLink,
                UseShellExecute = true
            });
            StatusText.Text = "Opening magnet link...";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open magnet link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Dictionary<string, string> LoadCookiesFromFile(string filename)
    {
        var cookies = new Dictionary<string, string>();
        if (File.Exists(filename))
        {
            foreach (string line in File.ReadAllLines(filename))
            {
                if (line.Contains("="))
                {
                    var parts = line.Split(new[] { '=' }, 2);
                    cookies[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }
        return cookies;
    }

    private List<SearchResult> ExtractTitles(IWebDriver driver)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        var titleElements = wait.Until(d => d.FindElements(By.CssSelector("div.wbr.t-title a.tLink")));
        var sizeElements = driver.FindElements(By.CssSelector("a.small.tr-dl.dl-stub"));

        var results = new List<SearchResult>();
        for (int i = 0; i < titleElements.Count; i++)
        {
            var element = titleElements[i];
            string size = i < sizeElements.Count ? sizeElements[i].Text.Trim() : "Unknown";
            
            results.Add(new SearchResult
            {
                Title = element.Text.Trim(),
                Link = element.GetAttribute("href"),
                TopicId = element.GetAttribute("data-topic_id"),
                Size = size
            });
        }

        return results;
    }

    private string ExtractMagnetLink(IWebDriver driver)
    {
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            var magnetLink = wait.Until(d => d.FindElement(By.CssSelector("a.magnet-link")));
            return magnetLink.GetAttribute("href") ?? string.Empty;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error extracting magnet link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return string.Empty;
        }
    }

    private int GetTotalPages(IWebDriver driver)
    {
        try
        {
            var pageLinks = driver.FindElements(By.CssSelector("a.pg[href*='start=']"));
            if (!pageLinks.Any())
                return 1;

            int maxPage = 1;
            foreach (var link in pageLinks)
            {
                if (int.TryParse(link.Text, out int pageNum))
                {
                    maxPage = Math.Max(maxPage, pageNum);
                }
            }
            return maxPage;
        }
        catch
        {
            return 1;
        }
    }

    private bool GoToPage(IWebDriver driver, int pageNum)
    {
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            var pageLinks = driver.FindElements(By.CssSelector("a.pg[href*='start=']"));
            var targetLink = pageLinks.FirstOrDefault(link => link.Text == pageNum.ToString());

            if (targetLink == null)
            {
                StatusText.Text = $"Could not find link for page {pageNum}";
                return false;
            }

            // Scroll the link into view
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", targetLink);
            System.Threading.Thread.Sleep(500); // Small delay to ensure scroll completes

            // Click the link
            targetLink.Click();

            // Wait for the page to load
            wait.Until(d => d.FindElements(By.CssSelector("div.wbr.t-title a.tLink")).Any());
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error going to page {pageNum}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        searchCancellation?.Cancel();
        pauseEvent?.Dispose();
        driver?.Quit();
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public class SearchResult
{
    public string Title { get; set; }
    public string Link { get; set; }
    public string TopicId { get; set; }
    public string Size { get; set; }
    public string ImageUrl { get; set; }
}
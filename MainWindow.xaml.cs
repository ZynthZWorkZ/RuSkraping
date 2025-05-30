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
        
        // Initialize driver on a background thread
        Task.Run(() =>
        {
            try
        {
            InitializeChromeDriver();
        }
        catch (Exception ex)
            {
                // Show error message on the UI thread
                Dispatcher.Invoke(() =>
        {
            MessageBox.Show($"Failed to initialize Chrome driver: {ex.Message}\n\nPlease make sure Chrome is installed and up to date.", 
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
                });
        }
        });
    }

    private void InitializeChromeDriver()
    {
        ChromeDriver tempDriver = null;
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
            tempDriver = new ChromeDriver(service, options);
            tempDriver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
            tempDriver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
            driver = tempDriver; // Assign to the field only after successful creation and setup
        }
        catch (WebDriverException ex)
        {
            // Dispose of tempDriver if it was created before the exception
            tempDriver?.Quit();
            tempDriver?.Dispose();
            throw new Exception($"Chrome driver initialization failed: {ex.Message}", ex);
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        // Access UI elements on the UI thread
        if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            MessageBox.Show("Please enter a search query.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Access UI elements on the UI thread
            SearchButton.IsEnabled = false;
            PauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            searchCancellation = new CancellationTokenSource();
            isPaused = false;
            pauseEvent.Set();

        // Create and show progress window on UI thread
        var progressWindow = new Progress();
        progressWindow.Owner = this;
        progressWindow.Show();
        progressWindow.StartFakeAnimation();

        // Run the search in a background thread
        await Task.Run(async () =>
        {
            try
            {
                await PerformSearch(searchCancellation.Token, progressWindow);
        }
        catch (OperationCanceledException)
            {
                // Update status text on UI thread
                await Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text = "Search stopped.";
                });
        }
        catch (Exception ex)
            {
                // Show error message on UI thread
                await Dispatcher.InvokeAsync(() =>
        {
            MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
        }
        finally
            {
                // Update UI elements and close window on UI thread
                await Dispatcher.InvokeAsync(() =>
        {
            SearchButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
                    progressWindow.Close();
                });
            }
        });
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!isPaused)
        {
            isPaused = true;
            pauseEvent.Reset();
            PauseButton.Content = "Resume";
            StatusText.Text = "Search paused...";
        }
        else
        {
            isPaused = false;
            pauseEvent.Set();
            PauseButton.Content = "Pause";
            StatusText.Text = "Resuming search...";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        searchCancellation?.Cancel();
        pauseEvent.Set(); // Ensure we're not stuck in a pause
        StatusText.Text = "Stopping search...";
        
        // Properly dispose of the driver when stopping
        if (driver != null)
        {
            try
            {
                driver.Quit();
                driver.Dispose();
                driver = null;
                
                // Reinitialize the driver for future searches
                Task.Run(() =>
                {
                    try
                    {
                        InitializeChromeDriver();
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Failed to reinitialize Chrome driver: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error closing Chrome driver: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async Task PerformSearch(CancellationToken cancellationToken, Progress progressWindow)
    {
        // Clear search results and update status on UI thread
        await Dispatcher.InvokeAsync(() =>
    {
        searchResults.Clear();
        StatusText.Text = "Searching...";
        GetMagnetButton.IsEnabled = false;
        OpenMagnetButton.IsEnabled = false;
        });

        try
        {
            // Selenium operations on background thread
            await Task.Run(() => driver.Navigate().GoToUrl("https://rutracker.org"));

            // Load and set cookies if enabled
            // Accessing UseCookiesCheckBox needs to be on the UI thread
            bool useCookies = await Dispatcher.InvokeAsync(() => UseCookiesCheckBox.IsChecked == true);

            if (useCookies)
            {
                var cookies = LoadCookiesFromFile("rutackercookies.txt");
                // Adding cookies to driver on background thread
                await Task.Run(() =>
                {
                    foreach (var cookie in cookies)
                {
                    driver.Manage().Cookies.AddCookie(new Cookie(cookie.Key, cookie.Value));
                }
                });
            }

            // Construct the target URL - SearchTextBox access on UI thread
            string baseUrl = "https://rutracker.org/forum/tracker.php?nm=";
            string searchText = await Dispatcher.InvokeAsync(() => SearchTextBox.Text);
            string encodedSearch = HttpUtility.UrlEncode(searchText);
            string targetUrl = baseUrl + encodedSearch;

            // Visit the first page on background thread
            await Task.Run(() => driver.Navigate().GoToUrl(targetUrl));

            // Get total number of pages on background thread
            int totalPages = await Task.Run(() => GetTotalPages(driver));
            // Update status text on UI thread
            await Dispatcher.InvokeAsync(() => StatusText.Text = $"Found {totalPages} pages of results");

            // Process each page
            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                // Check for cancellation and pause on background thread
                if (progressWindow.IsCancelled)
                {
                    throw new OperationCanceledException();
                }

                cancellationToken.ThrowIfCancellationRequested();
                pauseEvent.WaitOne(); // Wait if paused

                // StatusText update on UI thread
                await Dispatcher.InvokeAsync(() => StatusText.Text = $"Processing page {pageNum} of {totalPages}...");
                double progress = (double)pageNum / totalPages * 100;
                // progressWindow.UpdateProgress is designed to be thread-safe and uses Dispatcher.Invoke internally
                progressWindow.UpdateProgress(progress, $"Processing page {pageNum} of {totalPages}");

                // Extract titles from current page on background thread
                var pageResults = await Task.Run(() => ExtractTitles(driver));
                // Add results to searchResults (ObservableCollection) on UI thread
                foreach (var result in pageResults)
                {
                    await Dispatcher.InvokeAsync(() => searchResults.Add(result));
                }

                // If this isn't the last page, go to the next page on background thread
                if (pageNum < totalPages)
                {
                    bool success = await Task.Run(() => GoToPage(driver, pageNum + 1));
                    if (!success)
                    {
                        // Update status text on UI thread
                        await Dispatcher.InvokeAsync(() => StatusText.Text = $"Failed to go to page {pageNum + 1}");
                        break;
                    }
                    // Small delay on background thread
                    await Task.Delay(1000, cancellationToken);
                }
            }

            // Update status text on UI thread
            await Dispatcher.InvokeAsync(() => StatusText.Text = $"Search complete. Found {searchResults.Count} results.");
            
            // Only fetch images if the checkbox is checked - FetchImagesCheckBox access on UI thread
            bool fetchImages = await Dispatcher.InvokeAsync(() => FetchImagesCheckBox.IsChecked == true);

            if (fetchImages)
            {
                // Update status text on UI thread
                await Dispatcher.InvokeAsync(() => StatusText.Text = $"Search complete. Found {searchResults.Count} results. Fetching images...");
                // progressWindow.UpdateProgress is thread-safe
                progressWindow.UpdateProgress(0, "Fetching images...");
                await FetchImagesForResults(cancellationToken, progressWindow);
            }

            // Update status text on UI thread
            await Dispatcher.InvokeAsync(() => StatusText.Text = $"Search complete. Found {searchResults.Count} results.");
        }
        finally
        {
            // Update UI elements and close window on UI thread
            await Dispatcher.InvokeAsync(() =>
        {
            GetMagnetButton.IsEnabled = true;
            OpenMagnetButton.IsEnabled = true;
                progressWindow.Close();
            });
        }
    }

    private async Task FetchImagesForResults(CancellationToken cancellationToken, Progress progressWindow)
    {
        var tasks = new List<Task>();
        // Accessing searchResults.Count needs to be on the UI thread
        int totalResults = await Dispatcher.InvokeAsync(() => searchResults.Count);
        int processedResults = 0;

        foreach (var result in searchResults)
        {
            if (cancellationToken.IsCancellationRequested || progressWindow.IsCancelled) break;
            
            // Fetching image for result should be on background thread
            tasks.Add(Task.Run(async () =>
            {
                await FetchImageForResult(result, cancellationToken);
                // Updating processedResults and progress needs to be on UI thread for accurate display
                await Dispatcher.InvokeAsync(() =>
                {
                    processedResults++;
                    double progress = (double)processedResults / totalResults * 100;
                    progressWindow.UpdateProgress(progress, $"Fetching image {processedResults} of {totalResults}");
                });
            }));
            
            // Process in batches to avoid overwhelming the browser
            if (tasks.Count >= 5)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
                // Small delay on background thread
                await Task.Delay(500, cancellationToken);
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
            // Navigate to the result's page on background thread
            await Task.Run(() => driver.Navigate().GoToUrl(result.Link));
            
            // Wait for the page to load and find element on background thread
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            var imageElement = await Task.Run(() =>
            {
                try
                {
                    return wait.Until(d => 
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
                }
                catch
                {
                    return null;
                }
            });

            if (imageElement != null)
            {
                // Get attribute on background thread
                string imageUrl = await Task.Run(() => imageElement.GetAttribute("src"));
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    // Updating SearchResult.ImageUrl needs to be on UI thread as SearchResult is in ObservableCollection
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

    private string ExtractMagnetLink(IWebDriver driver)
    {
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            // Finding element on background thread
            var magnetLinkElement = wait.Until(d => d.FindElement(By.CssSelector("a.magnet-link")));
            // Getting attribute on background thread
            return magnetLinkElement.GetAttribute("href") ?? string.Empty;
        }
        catch (Exception ex)
        {
            // MessageBox.Show needs to be on UI thread
            Dispatcher.Invoke(() => MessageBox.Show($"Error extracting magnet link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            return string.Empty;
        }
    }

    private int GetTotalPages(IWebDriver driver)
    {
        try
        {
            // Finding elements on background thread
            var pageLinks = driver.FindElements(By.CssSelector("a.pg[href*='start=']"));
            if (!pageLinks.Any())
                return 1;

            int maxPage = 1;
            foreach (var link in pageLinks)
            {
                // Accessing link.Text needs to be on background thread if link element is from background thread
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
            // Finding elements on background thread
            var pageLinks = driver.FindElements(By.CssSelector("a.pg[href*='start=']"));
            // Finding target link on background thread
            var targetLink = pageLinks.FirstOrDefault(link => link.Text == pageNum.ToString());

            if (targetLink == null)
            {
                // StatusText update was already handled on UI thread in PerformSearch
                return false;
            }

            // Scroll the link into view on background thread
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", targetLink);
            System.Threading.Thread.Sleep(500); // Small delay on background thread

            // Click the link on background thread
            targetLink.Click();

            // Wait for the page to load on background thread
            wait.Until(d => d.FindElements(By.CssSelector("div.wbr.t-title a.tLink")).Any());
            return true;
        }
        catch (Exception ex)
        {
            // MessageBox.Show needs to be on UI thread
            Dispatcher.Invoke(() => MessageBox.Show($"Error going to page {pageNum}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            return false;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        searchCancellation?.Cancel();
        pauseEvent?.Dispose();
        
        // Properly dispose of the driver
        if (driver != null)
        {
            try
            {
                driver.Quit();
                driver.Dispose();
                driver = null;
            }
            catch (Exception ex)
            {
                // Log the error but don't prevent closing
                MessageBox.Show($"Error closing Chrome driver: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
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

    // Add Magnet Link related methods here if needed, ensuring thread safety.
    // For example, if GetMagnetButton_Click and OpenMagnetButton_Click interact with
    // searchResults or driver, they should use Dispatcher.Invoke/InvokeAsync or Task.Run.
    // Looking at the existing code, GetMagnetButton_Click and OpenMagnetButton_Click
    // already seem to be interacting with driver and currentMagnetLink (which isn't UI).
    // We should ensure GetMagnetButton_Click's driver interaction is backgrounded and
    // MessageBox.Show is UI-threaded.

    private async void GetMagnetButton_Click(object sender, RoutedEventArgs e)
    {
        // Access UI elements on UI thread
        if (ResultsListView.SelectedItem == null)
        {
            MessageBox.Show("Please select a result first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedResult = (SearchResult)ResultsListView.SelectedItem;
        
        // Update UI elements on UI thread
        StatusText.Text = "Getting magnet link...";
        GetMagnetButton.IsEnabled = false;
        OpenMagnetButton.IsEnabled = false;

        try
        {
            // Selenium operations on background thread
            string magnetLink = await Task.Run(() => ExtractMagnetLinkForSelection(selectedResult));

            if (!string.IsNullOrEmpty(magnetLink))
            {
                currentMagnetLink = magnetLink;
                // Set clipboard text and update status on UI thread
                Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(currentMagnetLink);
                StatusText.Text = "Magnet link copied to clipboard!";
                OpenMagnetButton.IsEnabled = true;
                });
            }
            else
            {
                // Update status on UI thread
                Dispatcher.Invoke(() => StatusText.Text = "Could not find magnet link.");
            }
        }
        catch (Exception ex)
        {
            // Show error message and update status on UI thread
            Dispatcher.Invoke(() =>
        {
            MessageBox.Show($"Error getting magnet link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Error getting magnet link.";
            });
        }
        finally
        {
            // Update UI element on UI thread
            Dispatcher.Invoke(() => GetMagnetButton.IsEnabled = true);
        }
    }

    // Helper method to extract magnet link for a specific result on a background thread
    private string ExtractMagnetLinkForSelection(SearchResult selectedResult)
    {
         // Navigate to the topic page on background thread
        driver.Navigate().GoToUrl(selectedResult.Link);
        
        // Wait for and extract the magnet link on background thread
        return ExtractMagnetLink(driver);
    }

    private void OpenMagnetButton_Click(object sender, RoutedEventArgs e)
    {
        // Access currentMagnetLink (not a UI element, so no dispatcher needed)
        if (string.IsNullOrEmpty(currentMagnetLink))
        {
            MessageBox.Show("No magnet link available.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Starting a process doesn't typically need dispatcher unless it interacts with UI elements immediately after.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = currentMagnetLink,
                UseShellExecute = true
            });
            // Update status on UI thread
            StatusText.Text = "Opening magnet link...";
        }
        catch (Exception ex)
        {
            // Show error message on UI thread
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

    private async void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        // Access UI elements on UI thread
        var button = sender as Button;
        var selectedResult = button?.DataContext as SearchResult;

        if (selectedResult == null)
        {
            return; // Should not happen if button is in ListView item template
        }

        // Create and show progress window on UI thread
        var progressWindow = new Progress();
        progressWindow.Owner = this;
        progressWindow.Show();
        progressWindow.StartFakeAnimation();
        
        string detailsText = "";

        try
        {
            // Selenium operations on background thread
            detailsText = await Task.Run(() =>
            {
                // Ensure driver is initialized before use
                if (driver == null)
                {
                     Dispatcher.Invoke(() => MessageBox.Show("Chrome driver is not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                     return "Error: Chrome driver not initialized.";
                }

                try
                {
                    driver.Navigate().GoToUrl(selectedResult.Link);

                    // Wait for the post body div to be present
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
                    var postBodyElement = wait.Until(d => d.FindElement(By.CssSelector("div.post_body")));

                    // Return the text content of the post body div
                    return postBodyElement.Text;
        }
        catch (Exception ex)
        {
                     Dispatcher.Invoke(() => MessageBox.Show($"Error scraping details: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                     return $"Error scraping details: {ex.Message}";
                }
            });
        }
        finally
        {
             // Close progress window on UI thread
            await Dispatcher.InvokeAsync(() =>
            {
                progressWindow.Close();
            });
        }

        // Show details in a new window on UI thread
        await Dispatcher.InvokeAsync(() =>
        {
            var detailsWindow = new Details();
            detailsWindow.Owner = this;
            detailsWindow.SetDetailsText(detailsText);
            detailsWindow.ShowDialog(); // Use ShowDialog to make it modal
        });
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
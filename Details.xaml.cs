using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using GTranslate.Translators;
using GTranslate;
using System.Text;

namespace RuSkraping
{
    /// <summary>
    /// Interaction logic for Details.xaml
    /// </summary>
    public partial class Details : Window
    {
        private readonly ITranslator _translator;
        private readonly ITranslator _transliterator;
        private string _originalText = string.Empty;
        private bool _isTranslated = false;
        private bool _isTransliterated = false;

        public Details()
        {
            InitializeComponent();
            // Use Bing Translator for translation
            _translator = new BingTranslator();
            // Use Yandex for transliteration
            _transliterator = new YandexTranslator();
        }

        // Method to set the details text
        public void SetDetailsText(string details)
        {
            _originalText = details;
            DetailsTextBlock.Text = details;
            _isTranslated = false;
            _isTransliterated = false;
        }

        // Method to set the details with image
        public void SetDetailsWithImage(string details, string imageUrl)
        {
            _originalText = details;
            DetailsTextBlock.Text = details;
            _isTranslated = false;
            _isTransliterated = false;
            
            // Load image if URL is provided
            if (!string.IsNullOrEmpty(imageUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imageUrl, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    
                    TorrentImage.Source = bitmap;
                    ImageBorder.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load image: {ex.Message}");
                    ImageBorder.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                ImageBorder.Visibility = Visibility.Collapsed;
            }
        }

        private async void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isTranslated || _isTransliterated)
                {
                    // If already translated or transliterated, switch back to original
                    DetailsTextBlock.Text = _originalText;
                    _isTranslated = false;
                    _isTransliterated = false;
                }
                else
                {
                    // Split text into lines (paragraphs)
                    string[] paragraphs = _originalText.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
                    StringBuilder translatedTextBuilder = new StringBuilder();

                    // Show loading state
                    DetailsTextBlock.Text = "Translating...";

                    foreach (var paragraph in paragraphs)
                    {
                        if (!string.IsNullOrWhiteSpace(paragraph))
                        {
                            // First try transliteration for the paragraph
                            string processedParagraph = paragraph;
                            try
                            {
                                var translitResult = await _transliterator.TransliterateAsync(paragraph, "en");
                                processedParagraph = translitResult.Transliteration;
                            }
                            catch (System.Exception translitEx)
                            {
                                // If transliteration fails, use the original paragraph for translation
                                System.Diagnostics.Debug.WriteLine($"Transliteration failed for paragraph: {translitEx.Message}");
                            }

                            // Then translate the potentially transliterated paragraph
                            try
                            {
                                var result = await _translator.TranslateAsync(processedParagraph, "en");
                                translatedTextBuilder.AppendLine(result.Translation);
                            }
                            catch (System.Exception translateEx)
                            {
                                // If translation fails for this paragraph, append the original paragraph
                                System.Diagnostics.Debug.WriteLine($"Translation failed for paragraph: {translateEx.Message}");
                                translatedTextBuilder.AppendLine(paragraph);
                            }
                        }
                        else
                        {
                            translatedTextBuilder.AppendLine(); // Maintain empty lines
                        }
                    }

                    // Update the text block with the combined translated text
                    DetailsTextBlock.Text = translatedTextBuilder.ToString().TrimEnd();
                    _isTranslated = true; // Assume translation is the final state after this process
                    _isTransliterated = false; // Reset transliteration state
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred during translation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DetailsTextBlock.Text = _originalText;
                _isTranslated = false;
                _isTransliterated = false;
            }
        }

        // Event handler for the close button
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Event handler for dragging the window
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
} 
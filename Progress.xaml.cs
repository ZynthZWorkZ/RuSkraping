using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Threading.Tasks;

namespace RuSkraping
{
    public partial class Progress : Window
    {
        private readonly double maxWidth;
        private bool isCancelled;
        private double currentProgress;
        private bool isAnimating;
        private Random random;

        public Progress()
        {
            InitializeComponent();
            maxWidth = 360; // Width of the progress bar container (400 - 40 for margins)
            isCancelled = false;
            currentProgress = 0;
            isAnimating = false;
            random = new Random();
        }

        public async void StartFakeAnimation()
        {
            isAnimating = true;
            currentProgress = 0;
            
            while (isAnimating && !isCancelled)
            {
                // Generate a random increment between 0.1 and 0.5
                double increment = random.NextDouble() * 0.4 + 0.1;
                
                // Ensure we don't go over 95% during fake animation
                if (currentProgress + increment > 95)
                {
                    currentProgress = 95;
                }
                else
                {
                    currentProgress += increment;
                }

                UpdateProgressBar(currentProgress);
                await Task.Delay(100); // Update every 100ms
            }
        }

        public void StopFakeAnimation()
        {
            isAnimating = false;
        }

        private void UpdateProgressBar(double percentage)
        {
            Dispatcher.Invoke(() =>
            {
                // Update progress percentage text
                ProgressText.Text = $"{percentage:F1}%";

                // Calculate the target width
                double targetWidth = (percentage / 100.0) * maxWidth;

                // Create the width animation
                var widthAnimation = new DoubleAnimation
                {
                    From = ProgressIndicator.Width,
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                // Apply the width animation
                ProgressIndicator.BeginAnimation(WidthProperty, widthAnimation);
            });
        }

        public void UpdateProgress(double percentage, string status = null)
        {
            if (isCancelled) return;

            Dispatcher.Invoke(() =>
            {
                // Update status text if provided
                if (!string.IsNullOrEmpty(status))
                {
                    StatusText.Text = status;
                }

                // Stop fake animation if it's running
                StopFakeAnimation();

                // Update progress percentage text
                ProgressText.Text = $"{percentage:F0}%";

                // Calculate the target width
                double targetWidth = (percentage / 100.0) * maxWidth;

                // Create the width animation
                var widthAnimation = new DoubleAnimation
                {
                    From = ProgressIndicator.Width,
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                // Apply the width animation
                ProgressIndicator.BeginAnimation(WidthProperty, widthAnimation);

                // Update current progress
                currentProgress = percentage;
            });
        }

        public bool IsCancelled => isCancelled;

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}

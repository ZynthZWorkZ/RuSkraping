using System.Windows;
using System.Windows.Input;

namespace RuSkraping;

/// <summary>
/// Interaction logic for MagnetInputDialog.xaml
/// </summary>
public partial class MagnetInputDialog : Window
{
    public string MagnetLink { get; private set; } = string.Empty;

    public MagnetInputDialog()
    {
        InitializeComponent();
        MagnetTextBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        MagnetLink = MagnetTextBox.Text.Trim();
        
        if (!string.IsNullOrEmpty(MagnetLink))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Please enter a magnet link.", "Validation", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            MagnetTextBox.Focus();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void MagnetTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, new RoutedEventArgs());
        }
        else if (e.Key == Key.Escape)
        {
            CancelButton_Click(sender, new RoutedEventArgs());
        }
    }
}

using System;
using System.Windows;
using System.Windows.Input;

namespace WallFlow.Windows;

public partial class BookmarkEditWindow : Window
{
    public string BookmarkName { get; private set; } = string.Empty;
    public string BookmarkUrl { get; private set; } = string.Empty;

    public BookmarkEditWindow(string currentName, string currentUrl)
    {
        InitializeComponent();
        
        NameTextBox.Text = currentName;
        UrlTextBox.Text = currentUrl;
        
        // Focus the name input on load
        Loaded += (s, e) => NameTextBox.Focus();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NameTextBox.Text.Trim();
        string url = UrlTextBox.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("El nombre del marcador no puede estar vacío.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("La dirección URL no puede estar vacía.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Simple validation or prepend protocol if missing
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        BookmarkName = name;
        BookmarkUrl = url;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveButton_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            CancelButton_Click(sender, e);
        }
    }
}

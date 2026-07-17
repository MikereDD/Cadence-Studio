using System.Windows;
using System.Windows.Input;

namespace CadenceStudio.App;

public partial class PlaylistRenameDialog : Window
{
    public PlaylistRenameDialog(string currentName)
    {
        InitializeComponent();
        PlaylistNameBox.Text = currentName ?? string.Empty;
        Loaded += (_, _) =>
        {
            PlaylistNameBox.Focus();
            PlaylistNameBox.SelectAll();
        };
    }

    public string PlaylistName => PlaylistNameBox.Text.Trim();

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PlaylistNameBox.Text))
        {
            ValidationText.Text = "Enter a playlist name.";
            ValidationText.Visibility = Visibility.Visible;
            PlaylistNameBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void PlaylistNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

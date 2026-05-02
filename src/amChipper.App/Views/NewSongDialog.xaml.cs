using System.Windows;
using System.Windows.Controls;
using amChipper.Core.Models;

namespace amChipper.App.Views;

/// <summary>
/// Represents the NewSongDialog component.
/// </summary>
public partial class NewSongDialog : Window
{
    /// <summary>
    /// Stores or exposes Options.
    /// </summary>
    public NewSongOptions Options { get; private set; } = NewSongOptions.Default;

    public NewSongDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Executes the Create_Click operation.
    /// </summary>
    private void Create_Click(object sender, RoutedEventArgs e)
    {
        Options = new NewSongOptions
        {
            Title = TitleBox.Text,
            Format = ParseFormat((FormatBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()),
            Channels = ParseInt(ChannelsBox.Text, 8),
            Patterns = ParseInt(PatternsBox.Text, 4),
            RowsPerPattern = ParseInt(RowsBox.Text, 64),
            RowsPerBeat = ParseInt(RowsPerBeatBox.Text, 4),
            Bpm = ParseInt(BpmBox.Text, 125),
            InitialSpeed = ParseInt(SpeedBox.Text, 6),
            IncludeDefaultSamples = SamplesBox.IsChecked == true,
            CreatePlaylistBlocks = BlocksBox.IsChecked == true
        }.Normalize();

        DialogResult = true;
    }

    /// <summary>
    /// Executes the ParseInt operation.
    /// </summary>
    private static int ParseInt(string? text, int fallback) =>
        int.TryParse(text, out int value) ? value : fallback;

    /// <summary>
    /// Executes the ParseFormat operation.
    /// </summary>
    private static ModuleFormat ParseFormat(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out ModuleFormat format) ? format : ModuleFormat.AmChip;
}

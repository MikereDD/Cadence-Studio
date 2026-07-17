using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CadenceStudio.Core.Models;
using TagFile = TagLib.File;

namespace CadenceStudio.App;

public partial class MetadataWorkshopDialog : Window
{
    private readonly Track _track;
    private readonly Func<bool, CancellationToken, Task<TrackEnrichment>> _proposalLoader;
    private readonly Func<TrackEnrichment, MusicBrainzReleaseCandidate, bool, CancellationToken, Task<TrackEnrichment>> _releaseSelector;
    private TrackEnrichment? _enrichment;
    private byte[]? _preferredArtwork;
    private CancellationTokenSource? _proposalCancellation;
    private bool _updatingReleaseCandidates;
    private string _selectedReleaseId = string.Empty;

    public MetadataWorkshopDialog(
        Track track,
        TrackEnrichment? enrichment,
        Func<bool, CancellationToken, Task<TrackEnrichment>> proposalLoader,
        Func<TrackEnrichment, MusicBrainzReleaseCandidate, bool, CancellationToken, Task<TrackEnrichment>> releaseSelector)
    {
        InitializeComponent();
        _track = track;
        _enrichment = enrichment;
        _proposalLoader = proposalLoader;
        _releaseSelector = releaseSelector;
        _preferredArtwork = GetPreferredArtwork(enrichment);
        LoadDisplay();
        Loaded += MetadataWorkshopDialog_Loaded;
        Closed += (_, _) =>
        {
            _proposalCancellation?.Cancel();
            _proposalCancellation?.Dispose();
        };
    }

    public bool TagsChanged { get; private set; }

    private async void MetadataWorkshopDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshProposalAsync(forceRefresh: true);
    }

    private void LoadDisplay()
    {
        FileNameText.Text = Path.GetFileName(_track.FilePath);
        FilePathText.Text = _track.FilePath;
        BuildCurrentFields();
        RestoreCurrentValues();
        ApplyProposalDisplay();
    }

    private void BuildCurrentFields()
    {
        CurrentFieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        CurrentFieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fields = new (string Label, string Value)[]
        {
            ("Title", _track.Title), ("Artist", _track.Artist), ("Album artist", _track.AlbumArtist),
            ("Album", _track.Album), ("Genre", _track.Genre), ("Year", Format(_track.Year)),
            ("Track", Pair(_track.TrackNumber, _track.TrackCount)), ("Disc", Pair(_track.DiscNumber, _track.DiscCount)),
            ("Audio", _track.FormatDescription)
        };

        for (var i = 0; i < fields.Length; i++)
        {
            CurrentFieldsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock
            {
                Text = fields[i].Label,
                Margin = new Thickness(0, i == 0 ? 0 : 8, 10, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush")
            };
            var value = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(fields[i].Value) ? "—" : fields[i].Value,
                Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            CurrentFieldsGrid.Children.Add(label);
            CurrentFieldsGrid.Children.Add(value);
        }
    }

    private void RestoreCurrentValues()
    {
        TitleBox.Text = _track.Title;
        ArtistBox.Text = _track.Artist;
        AlbumArtistBox.Text = _track.AlbumArtist;
        AlbumBox.Text = _track.Album;
        GenreBox.Text = _track.Genre;
        YearBox.Text = Format(_track.Year);
        TrackNumberBox.Text = Format(_track.TrackNumber);
        TrackCountBox.Text = Format(_track.TrackCount);
        DiscNumberBox.Text = Format(_track.DiscNumber);
        DiscCountBox.Text = Format(_track.DiscCount);
        StatusText.Text = "Current values restored. No changes have been written.";
    }

    private async Task RefreshProposalAsync(bool forceRefresh)
    {
        _proposalCancellation?.Cancel();
        _proposalCancellation?.Dispose();
        _proposalCancellation = new CancellationTokenSource();
        var token = _proposalCancellation.Token;

        SearchProposalButton.IsEnabled = false;
        ApplyProposalButton.IsEnabled = false;
        MatchStatusText.Text = "Searching MusicBrainz and Cover Art Archive…";
        ProposalReleaseText.Text = "Selecting the best release using album, year, track count, status, and artwork availability.";
        ProposalWarningText.Text = string.Empty;
        StatusText.Text = "Refreshing the MusicBrainz proposal…";

        try
        {
            _enrichment = await _proposalLoader(forceRefresh, token);
            _preferredArtwork = GetPreferredArtwork(_enrichment);
            ApplyProposalDisplay();
            StatusText.Text = _enrichment.HasMusicBrainzMatch
                ? "Proposal loaded. Review the selected release and artwork before applying or writing."
                : "No suitable MusicBrainz proposal was found. Current tags remain untouched.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            MatchStatusText.Text = "MusicBrainz proposal lookup failed";
            ProposalReleaseText.Text = exception.Message;
            ProposalWarningText.Text = "No changes have been written.";
            StatusText.Text = "Proposal lookup failed. Check the application log for details.";
        }
        finally
        {
            SearchProposalButton.IsEnabled = true;
            ApplyProposalButton.IsEnabled = _enrichment?.HasMusicBrainzMatch == true;
        }
    }

    private void ApplyProposalDisplay()
    {
        MatchStatusText.Text = _enrichment?.MusicBrainzStatus ?? "No MusicBrainz proposal loaded for this track";
        ApplyProposalButton.IsEnabled = _enrichment?.HasMusicBrainzMatch == true;

        var releaseSummary = BuildReleaseSummary(_enrichment);
        ProposalReleaseText.Text = releaseSummary;
        ProposalWarningText.Text = BuildProposalWarning(_enrichment);
        ProposalWarningBorder.Visibility = string.IsNullOrWhiteSpace(ProposalWarningText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        PopulateReleaseCandidates();
        _preferredArtwork = GetPreferredArtwork(_enrichment);
        var hasArtwork = TrySetArtwork(_preferredArtwork);
        EmbedArtworkCheck.IsEnabled = hasArtwork;
        EmbedArtworkCheck.IsChecked = hasArtwork;
        ArtworkHealthText.Text = hasArtwork
            ? $"Usable artwork ready • {GetArtworkSource()} • {_preferredArtwork!.Length / 1024:N0} KB"
            : _track.HasArtwork
                ? $"Usable local artwork detected • {_track.ArtworkSource} • {_track.ArtworkBytes!.Length / 1024:N0} KB"
                : _enrichment?.CoverArtLookupCompleted == true
                    ? $"No decodable artwork was returned • {_enrichment.CoverArtSource}"
                    : "No usable artwork is currently available.";
    }

    private string GetArtworkSource() => _enrichment?.CoverArtBytes is { Length: > 0 }
        ? _enrichment.CoverArtSource
        : _track.ArtworkSource;

    private byte[]? GetPreferredArtwork(TrackEnrichment? enrichment) =>
        enrichment?.CoverArtBytes is { Length: > 0 } online ? online : _track.ArtworkBytes;

    private static string BuildReleaseSummary(TrackEnrichment? enrichment)
    {
        if (enrichment is null || !enrichment.HasMusicBrainzMatch)
        {
            return "No selected MusicBrainz release.";
        }

        var details = new List<string>();
        AddDetail(details, enrichment.CanonicalAlbum);
        AddDetail(details, enrichment.SelectedReleaseDate);
        AddDetail(details, enrichment.SelectedReleaseCountry);
        AddDetail(details, enrichment.SelectedReleaseFormat);
        if (enrichment.SelectedReleaseTrackCount is > 0)
        {
            details.Add($"{enrichment.SelectedReleaseTrackCount} tracks");
        }
        AddDetail(details, enrichment.SelectedReleaseStatus);
        return details.Count == 0 ? "MusicBrainz recording matched, but release details were unavailable." : string.Join(" • ", details);
    }

    private string BuildProposalWarning(TrackEnrichment? enrichment)
    {
        if (enrichment is null || !enrichment.HasMusicBrainzMatch)
        {
            return string.Empty;
        }

        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(_track.Album) &&
            !string.Equals(Normalize(_track.Album), Normalize(enrichment.CanonicalAlbum), StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Album differs: current “{_track.Album}” vs proposal “{enrichment.CanonicalAlbum}”.");
        }

        if (_track.Year is > 0 && TryGetYear(enrichment.SelectedReleaseDate, out var proposalYear) && proposalYear != _track.Year.Value)
        {
            warnings.Add($"Year differs: current {_track.Year.Value} vs proposal {proposalYear}.");
        }

        if (_track.TrackCount is > 0 && enrichment.SelectedReleaseTrackCount is > 0 &&
            _track.TrackCount.Value != enrichment.SelectedReleaseTrackCount.Value)
        {
            warnings.Add($"Track total differs: current {_track.TrackCount.Value} vs proposal {enrichment.SelectedReleaseTrackCount.Value}.");
        }

        return warnings.Count == 0
            ? string.Empty
            : "Review before applying: " + string.Join(" ", warnings);
    }

    private void PopulateReleaseCandidates()
    {
        _updatingReleaseCandidates = true;
        try
        {
            ReleaseCandidateBox.ItemsSource = _enrichment?.ReleaseCandidates ?? [];
            if (_enrichment is null || _enrichment.ReleaseCandidates.Count == 0)
            {
                ReleaseCandidateBox.SelectedItem = null;
                ReleaseCandidateBox.IsEnabled = false;
                return;
            }

            ReleaseCandidateBox.IsEnabled = true;
            var preferredReleaseId = string.IsNullOrWhiteSpace(_selectedReleaseId)
                ? _enrichment.MusicBrainzReleaseId
                : _selectedReleaseId;
            ReleaseCandidateBox.SelectedItem = _enrichment.ReleaseCandidates.FirstOrDefault(candidate =>
                string.Equals(candidate.ReleaseId, preferredReleaseId, StringComparison.OrdinalIgnoreCase))
                ?? _enrichment.ReleaseCandidates[0];
        }
        finally
        {
            _updatingReleaseCandidates = false;
        }
    }

    private async void ReleaseCandidateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingReleaseCandidates || _enrichment is null || ReleaseCandidateBox.SelectedItem is not MusicBrainzReleaseCandidate candidate)
        {
            return;
        }

        await LoadSelectedReleaseAsync(candidate);
    }

    private async Task LoadSelectedReleaseAsync(MusicBrainzReleaseCandidate candidate)
    {
        if (_enrichment is null)
        {
            return;
        }

        _proposalCancellation?.Cancel();
        _proposalCancellation?.Dispose();
        _proposalCancellation = new CancellationTokenSource();
        var token = _proposalCancellation.Token;

        ReleaseCandidateBox.IsHitTestVisible = false;
        SearchProposalButton.IsEnabled = false;
        ApplyProposalButton.IsEnabled = false;
        StatusText.Text = $"Loading selected release and artwork: {candidate.Title}…";

        try
        {
            _selectedReleaseId = candidate.ReleaseId;
            _enrichment = await _releaseSelector(_enrichment, candidate, true, token);
            _preferredArtwork = GetPreferredArtwork(_enrichment);
            ApplyProposalDisplay();
            ApplySelectedReleaseToEditableFields();
            StatusText.Text = $"Selected release loaded: {candidate.DisplayLabel}. Track total and artwork were refreshed.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not load the selected release: {exception.Message}";
        }
        finally
        {
            SearchProposalButton.IsEnabled = true;
            ApplyProposalButton.IsEnabled = _enrichment?.HasMusicBrainzMatch == true;
            ReleaseCandidateBox.IsHitTestVisible = true;
            ReleaseCandidateBox.IsEnabled = _enrichment?.ReleaseCandidates.Count > 0;
        }
    }

    private async void SearchProposal_Click(object sender, RoutedEventArgs e)
    {
        if (ReleaseCandidateBox.SelectedItem is MusicBrainzReleaseCandidate candidate)
        {
            await LoadSelectedReleaseAsync(candidate);
            return;
        }

        await RefreshProposalAsync(forceRefresh: true);
    }

    private void UseProposal_Click(object sender, RoutedEventArgs e)
    {
        if (_enrichment is null || !_enrichment.HasMusicBrainzMatch)
        {
            StatusText.Text = "No MusicBrainz match is loaded. Refresh the proposal first.";
            return;
        }

        ApplySelectedReleaseToEditableFields();
        StatusText.Text = "MusicBrainz proposal applied to the editable fields. Review every field before writing.";
    }

    private void ApplySelectedReleaseToEditableFields()
    {
        if (_enrichment is null)
        {
            return;
        }

        TitleBox.Text = ValueOr(_enrichment.CanonicalTitle, _track.Title);
        ArtistBox.Text = ValueOr(_enrichment.CanonicalArtist, _track.Artist);
        AlbumArtistBox.Text = ValueOr(_enrichment.CanonicalArtist, _track.AlbumArtist);
        AlbumBox.Text = ValueOr(_enrichment.CanonicalAlbum, _track.Album);
        if (TryGetYear(_enrichment.SelectedReleaseDate, out var selectedYear) ||
            TryGetYear(_enrichment.FirstReleaseDate, out selectedYear))
        {
            YearBox.Text = selectedYear.ToString(CultureInfo.InvariantCulture);
        }

        TrackNumberBox.Text = _track.TrackNumber is > 0
            ? _track.TrackNumber.Value.ToString(CultureInfo.InvariantCulture)
            : TrackNumberBox.Text;
        if (_enrichment.SelectedReleaseTrackCount is > 0)
        {
            TrackCountBox.Text = _enrichment.SelectedReleaseTrackCount.Value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void RestoreCurrent_Click(object sender, RoutedEventArgs e) => RestoreCurrentValues();

    private void OpenAlbumWorkshop_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AlbumMetadataWorkshopDialog(_track, _enrichment, _proposalLoader, _releaseSelector)
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.TagsChanged)
        {
            TagsChanged = true;
            StatusText.Text = "Album tags were written and verified. Reopen the workshop to refresh the single-track snapshot.";
        }
    }

    private void Write_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ValidateWritable();
            var backup = CreateBackup(_track.FilePath);
            using (var file = TagFile.Create(_track.FilePath))
            {
                file.Tag.Title = Clean(TitleBox.Text);
                file.Tag.Performers = SplitValues(ArtistBox.Text);
                file.Tag.AlbumArtists = SplitValues(AlbumArtistBox.Text);
                file.Tag.Album = Clean(AlbumBox.Text);
                file.Tag.Genres = SplitValues(GenreBox.Text);
                file.Tag.Year = ParseUInt(YearBox.Text, "Year");
                file.Tag.Track = ParseUInt(TrackNumberBox.Text, "Track number");
                file.Tag.TrackCount = ParseUInt(TrackCountBox.Text, "Track total");
                file.Tag.Disc = ParseUInt(DiscNumberBox.Text, "Disc number");
                file.Tag.DiscCount = ParseUInt(DiscCountBox.Text, "Disc total");
                if (EmbedArtworkCheck.IsChecked == true && _preferredArtwork is { Length: > 0 })
                {
                    file.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(_preferredArtwork))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        MimeType = DetectMime(_preferredArtwork),
                        Description = "Front cover"
                    }];
                }
                file.Save();
            }

            using (var verify = TagFile.Create(_track.FilePath))
            {
                if (!string.Equals(verify.Tag.Title ?? string.Empty, Clean(TitleBox.Text), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Verification failed: the saved title did not match the approved value.");
                }
                if (EmbedArtworkCheck.IsChecked == true && verify.Tag.Pictures.Length == 0)
                {
                    throw new InvalidOperationException("Verification failed: artwork was approved but no embedded picture was found after saving.");
                }
            }

            TagsChanged = true;
            StatusText.Text = $"Tags written and verified. Backup: {backup}";
            MessageBox.Show(this, "Tags were backed up, written, and verified successfully.", "Metadata Workshop", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            MessageBox.Show(this, exception.Message, "Metadata Workshop", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ValidateWritable()
    {
        if (!System.IO.File.Exists(_track.FilePath)) throw new FileNotFoundException("The selected audio file no longer exists.", _track.FilePath);
        var info = new FileInfo(_track.FilePath);
        if (info.IsReadOnly) throw new InvalidOperationException("The selected audio file is read-only.");
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) throw new InvalidOperationException("Title cannot be empty.");
        if (EmbedArtworkCheck.IsChecked == true && _preferredArtwork is not { Length: > 0 })
            throw new InvalidOperationException("Artwork embedding was selected, but no valid artwork is available.");
    }

    private static string CreateBackup(string filePath)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CadenceStudio", "tag-backups", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, Path.GetFileName(filePath));
        System.IO.File.Copy(filePath, destination, overwrite: false);
        return destination;
    }

    private bool TrySetArtwork(byte[]? bytes)
    {
        ArtworkImage.Source = null;
        if (bytes is not { Length: > 0 }) return false;
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            ArtworkImage.Source = image;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static uint ParseUInt(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (!uint.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"{label} must be a whole number.");
        return value;
    }

    private static bool TryGetYear(string? value, out int year)
    {
        year = 0;
        return !string.IsNullOrWhiteSpace(value) && value.Length >= 4 &&
               int.TryParse(value[..4], NumberStyles.None, CultureInfo.InvariantCulture, out year);
    }

    private static void AddDetail(ICollection<string> target, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) target.Add(value.Trim());
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string[] SplitValues(string? value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
    private static string ValueOr(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string Format(int? value) => value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    private static string Pair(int? value, int? total) => value is > 0 ? total is > 0 ? $"{value}/{total}" : value.ToString()! : string.Empty;
    private static string DetectMime(byte[] bytes) => bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 ? "image/png" : "image/jpeg";
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

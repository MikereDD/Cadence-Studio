using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CadenceStudio.Core.Models;
using TagFile = TagLib.File;

namespace CadenceStudio.App;

public partial class AlbumMetadataWorkshopDialog : Window
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".wma"
    };

    private readonly Track _seedTrack;
    private readonly Func<bool, CancellationToken, Task<TrackEnrichment>> _proposalLoader;
    private readonly Func<TrackEnrichment, MusicBrainzReleaseCandidate, bool, CancellationToken, Task<TrackEnrichment>> _releaseSelector;
    private TrackEnrichment? _enrichment;
    private byte[]? _artwork;
    private CancellationTokenSource? _cancellation;
    private bool _updatingReleaseCandidates;
    private string _selectedReleaseId = string.Empty;

    public AlbumMetadataWorkshopDialog(
        Track seedTrack,
        TrackEnrichment? enrichment,
        Func<bool, CancellationToken, Task<TrackEnrichment>> proposalLoader,
        Func<TrackEnrichment, MusicBrainzReleaseCandidate, bool, CancellationToken, Task<TrackEnrichment>> releaseSelector)
    {
        InitializeComponent();
        _seedTrack = seedTrack;
        _enrichment = enrichment;
        _proposalLoader = proposalLoader;
        _releaseSelector = releaseSelector;
        DataContext = this;
        LoadAlbumFiles();
        RestoreAlbumFields();
        Loaded += async (_, _) => await RefreshProposalAsync(true);
        Closed += (_, _) => { _cancellation?.Cancel(); _cancellation?.Dispose(); };
    }

    public ObservableCollection<AlbumTagRow> AlbumRows { get; } = [];
    public bool TagsChanged { get; private set; }

    private void LoadAlbumFiles()
    {
        var folder = Path.GetDirectoryName(_seedTrack.FilePath) ?? throw new InvalidOperationException("The selected file has no parent folder.");
        AlbumFolderText.Text = new DirectoryInfo(folder).Name;
        AlbumPathText.Text = folder;

        var rows = Directory.EnumerateFiles(folder)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .Select(ReadRow)
            .OrderBy(row => row.ExistingTrackNumber == 0 ? int.MaxValue : row.ExistingTrackNumber)
            .ThenBy(row => row.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].ProposedTrackNumber == 0)
            {
                rows[i].ProposedTrackNumber = i + 1;
            }
            AlbumRows.Add(rows[i]);
        }

        ScanSummaryText.Text = $"{AlbumRows.Count} supported audio files found • review-first batch mode";
        StatusText.Text = AlbumRows.Count == 0 ? "No supported audio files were found in this folder." : "Album files loaded. MusicBrainz proposal is being prepared.";
    }

    private AlbumTagRow ReadRow(string path)
    {
        try
        {
            using var file = TagFile.Create(path);
            return new AlbumTagRow
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                ExistingTrackNumber = (int)file.Tag.Track,
                ProposedTrackNumber = (int)file.Tag.Track,
                ProposedTitle = string.IsNullOrWhiteSpace(file.Tag.Title) ? Path.GetFileNameWithoutExtension(path) : file.Tag.Title,
                ProposedArtist = file.Tag.Performers.FirstOrDefault() ?? _seedTrack.Artist,
                CurrentSummary = $"{file.Tag.Title ?? "—"} • track {(file.Tag.Track == 0 ? "—" : file.Tag.Track)} / {(file.Tag.TrackCount == 0 ? "—" : file.Tag.TrackCount)}",
                Status = "Ready"
            };
        }
        catch (Exception ex)
        {
            return new AlbumTagRow
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                ProposedTitle = Path.GetFileNameWithoutExtension(path),
                ProposedArtist = _seedTrack.Artist,
                IsSelected = false,
                Status = $"Read error: {ex.Message}"
            };
        }
    }

    private void RestoreAlbumFields()
    {
        AlbumBox.Text = _seedTrack.Album;
        AlbumArtistBox.Text = string.IsNullOrWhiteSpace(_seedTrack.AlbumArtist) ? _seedTrack.Artist : _seedTrack.AlbumArtist;
        YearBox.Text = _seedTrack.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        GenreBox.Text = _seedTrack.Genre;
        foreach (var row in AlbumRows)
        {
            row.Status = row.IsSelected ? "Ready" : row.Status;
        }
    }

    private async Task RefreshProposalAsync(bool forceRefresh)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        RefreshProposalButton.IsEnabled = false;
        ApplyReleaseButton.IsEnabled = false;
        ReleaseCandidateBox.IsHitTestVisible = false;
        StatusText.Text = "Searching MusicBrainz and Cover Art Archive for the album…";

        try
        {
            _enrichment = await _proposalLoader(forceRefresh, token);
            _selectedReleaseId = _enrichment.MusicBrainzReleaseId;
            _artwork = GetPreferredArtwork(_enrichment);
            ApplyEnrichmentToHeader();
            ApplyReleaseToAlbum();
            TracksGrid.Items.Refresh();
            StatusText.Text = _enrichment.HasMusicBrainzMatch
                ? "Album proposal loaded. Choose a release candidate, then review every selected row before writing."
                : "No suitable MusicBrainz proposal was found. Local album values remain untouched.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText.Text = $"MusicBrainz album lookup failed: {ex.Message}";
        }
        finally
        {
            RefreshProposalButton.IsEnabled = true;
            ApplyReleaseButton.IsEnabled = _enrichment?.HasMusicBrainzMatch == true;
            ReleaseCandidateBox.IsHitTestVisible = true;
            ReleaseCandidateBox.IsEnabled = _enrichment?.ReleaseCandidates.Count > 0;
        }
    }

    private void ApplyEnrichmentToHeader()
    {
        if (_enrichment is null || !_enrichment.HasMusicBrainzMatch)
        {
            ReleaseSummaryText.Text = "No suitable MusicBrainz release was found. Local values remain available for batch editing.";
        }
        else
        {
            var parts = new List<string>();
            Add(parts, _enrichment.CanonicalAlbum);
            Add(parts, _enrichment.SelectedReleaseDate);
            Add(parts, _enrichment.SelectedReleaseCountry);
            Add(parts, _enrichment.SelectedReleaseFormat);
            if (_enrichment.SelectedReleaseTrackCount is > 0) parts.Add($"{_enrichment.SelectedReleaseTrackCount} tracks");
            Add(parts, _enrichment.SelectedReleaseStatus);
            ReleaseSummaryText.Text = string.Join(" • ", parts);
        }
        PopulateReleaseCandidates();
        EmbedArtworkCheck.IsEnabled = TrySetArtwork(_artwork);
        EmbedArtworkCheck.IsChecked = EmbedArtworkCheck.IsEnabled;
    }

    private byte[]? GetPreferredArtwork(TrackEnrichment? enrichment) =>
        enrichment?.CoverArtBytes is { Length: > 0 } online ? online : _seedTrack.ArtworkBytes;

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
        if (_updatingReleaseCandidates || _enrichment is null ||
            ReleaseCandidateBox.SelectedItem is not MusicBrainzReleaseCandidate candidate)
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

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        ReleaseCandidateBox.IsHitTestVisible = false;
        RefreshProposalButton.IsEnabled = false;
        ApplyReleaseButton.IsEnabled = false;
        StatusText.Text = $"Loading selected album release and artwork: {candidate.Title}…";

        try
        {
            _selectedReleaseId = candidate.ReleaseId;
            _enrichment = await _releaseSelector(_enrichment, candidate, true, token);
            _artwork = GetPreferredArtwork(_enrichment);
            ApplyEnrichmentToHeader();
            ApplyReleaseToAlbum();
            TracksGrid.Items.Refresh();
            StatusText.Text = $"Selected album release loaded: {candidate.DisplayLabel}. Album fields, track total, and artwork were refreshed.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load the selected album release: {ex.Message}";
        }
        finally
        {
            RefreshProposalButton.IsEnabled = true;
            ApplyReleaseButton.IsEnabled = _enrichment?.HasMusicBrainzMatch == true;
            ReleaseCandidateBox.IsHitTestVisible = true;
            ReleaseCandidateBox.IsEnabled = _enrichment?.ReleaseCandidates.Count > 0;
        }
    }

    private void ApplyReleaseToAlbum()
    {
        if (_enrichment is null) return;
        AlbumBox.Text = ValueOr(_enrichment.CanonicalAlbum, AlbumBox.Text);
        AlbumArtistBox.Text = ValueOr(_enrichment.CanonicalArtist, AlbumArtistBox.Text);
        if (TryGetYear(_enrichment.SelectedReleaseDate, out var year) || TryGetYear(_enrichment.FirstReleaseDate, out year))
        {
            YearBox.Text = year.ToString(CultureInfo.InvariantCulture);
        }
        var total = _enrichment.SelectedReleaseTrackCount is > 0 ? _enrichment.SelectedReleaseTrackCount.Value : AlbumRows.Count;
        foreach (var row in AlbumRows)
        {
            row.ProposedTrackTotal = total;
            row.Status = row.IsSelected ? "Proposal ready" : "Skipped";
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ReleaseCandidateBox.SelectedItem is MusicBrainzReleaseCandidate candidate)
        {
            await LoadSelectedReleaseAsync(candidate);
            return;
        }

        await RefreshProposalAsync(true);
    }

    private void ApplyRelease_Click(object sender, RoutedEventArgs e)
    {
        ApplyReleaseToAlbum();
        TracksGrid.Items.Refresh();
        StatusText.Text = "Selected release values applied across the album preview.";
    }
    private void RestoreLocal_Click(object sender, RoutedEventArgs e) { RestoreAlbumFields(); TracksGrid.Items.Refresh(); StatusText.Text = "Local album values restored. Nothing has been written."; }
    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var row in AlbumRows) row.IsSelected = true; TracksGrid.Items.Refresh(); }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var row in AlbumRows) row.IsSelected = false; TracksGrid.Items.Refresh(); }

    private void WriteAlbum_Click(object sender, RoutedEventArgs e)
    {
        var selected = AlbumRows.Where(row => row.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one track to write.", "Album Metadata Workshop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(AlbumBox.Text))
        {
            MessageBox.Show(this, "Album cannot be empty.", "Album Metadata Workshop", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CadenceStudio", "tag-backups", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture), Sanitize(Path.GetFileName(AlbumPathText.Text)));
        Directory.CreateDirectory(backupRoot);
        var written = 0;
        var failures = new List<string>();

        foreach (var row in selected)
        {
            try
            {
                File.Copy(row.FilePath, Path.Combine(backupRoot, row.FileName), overwrite: false);
                using (var file = TagFile.Create(row.FilePath))
                {
                    file.Tag.Title = row.ProposedTitle.Trim();
                    file.Tag.Performers = SplitValues(row.ProposedArtist);
                    file.Tag.AlbumArtists = SplitValues(AlbumArtistBox.Text);
                    file.Tag.Album = AlbumBox.Text.Trim();
                    file.Tag.Genres = SplitValues(GenreBox.Text);
                    file.Tag.Year = ParseUInt(YearBox.Text);
                    file.Tag.Track = (uint)Math.Max(0, row.ProposedTrackNumber);
                    file.Tag.TrackCount = (uint)Math.Max(0, row.ProposedTrackTotal > 0 ? row.ProposedTrackTotal : selected.Count);
                    if (EmbedArtworkCheck.IsChecked == true && _artwork is { Length: > 0 })
                    {
                        file.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(_artwork))
                        {
                            Type = TagLib.PictureType.FrontCover,
                            MimeType = DetectMime(_artwork),
                            Description = "Front cover"
                        }];
                    }
                    file.Save();
                }

                using var verify = TagFile.Create(row.FilePath);
                if (!string.Equals(verify.Tag.Title ?? string.Empty, row.ProposedTitle.Trim(), StringComparison.Ordinal) ||
                    !string.Equals(verify.Tag.Album ?? string.Empty, AlbumBox.Text.Trim(), StringComparison.Ordinal) ||
                    verify.Tag.Track != (uint)Math.Max(0, row.ProposedTrackNumber))
                {
                    throw new InvalidOperationException("Read-back verification did not match the approved values.");
                }
                if (EmbedArtworkCheck.IsChecked == true && verify.Tag.Pictures.Length == 0)
                {
                    throw new InvalidOperationException("Artwork verification failed.");
                }
                row.Status = "Written + verified";
                written++;
            }
            catch (Exception ex)
            {
                row.Status = "Failed";
                failures.Add($"{row.FileName}: {ex.Message}");
            }
        }

        TracksGrid.Items.Refresh();
        TagsChanged = written > 0;
        StatusText.Text = $"Album write complete: {written} verified, {failures.Count} failed. Backup: {backupRoot}";
        var message = failures.Count == 0
            ? $"{written} tracks were backed up, written, and verified successfully."
            : $"{written} tracks succeeded and {failures.Count} failed. Review the status column and application log.";
        MessageBox.Show(this, message, "Album Metadata Workshop", MessageBoxButton.OK, failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private bool TrySetArtwork(byte[]? bytes)
    {
        ArtworkImage.Source = null;
        if (bytes is not { Length: > 0 }) return false;
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            ArtworkImage.Source = image;
            return true;
        }
        catch { return false; }
    }

    private static uint ParseUInt(string? text) => uint.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static string[] SplitValues(string? value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static string ValueOr(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static bool TryGetYear(string? value, out int year) { year = 0; return !string.IsNullOrWhiteSpace(value) && value.Length >= 4 && int.TryParse(value[..4], out year); }
    private static void Add(ICollection<string> target, string? value) { if (!string.IsNullOrWhiteSpace(value)) target.Add(value.Trim()); }
    private static string DetectMime(byte[] bytes) => bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 ? "image/png" : "image/jpeg";
    private static string Sanitize(string value) => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class AlbumTagRow
{
    public bool IsSelected { get; set; } = true;
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public int ExistingTrackNumber { get; init; }
    public int ProposedTrackNumber { get; set; }
    public int ProposedTrackTotal { get; set; }
    public string ProposedTitle { get; set; } = string.Empty;
    public string ProposedArtist { get; set; } = string.Empty;
    public string CurrentSummary { get; init; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

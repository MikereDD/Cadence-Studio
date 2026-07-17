using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CadenceStudio.App.Mvvm;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Core.Models;

namespace CadenceStudio.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IPlaylistService _playlistService;
    private string _queueSearchText = string.Empty;
    private string _playlistNameText = string.Empty;
    private string _playlistSearchText = string.Empty;
    private SavedPlaylist? _selectedPlaylist;
    private Track? _selectedPlaylistTrack;
    private bool _playlistsInitialized;

    public ICollectionView QueueView { get; private set; } = null!;
    public ICollectionView PlaylistsView { get; private set; } = null!;
    public ObservableCollection<SavedPlaylist> Playlists { get; private set; } = null!;
    public ObservableCollection<Track> SelectedPlaylistTracks { get; private set; } = null!;

    public RelayCommand CreatePlaylistCommand { get; private set; } = null!;
    public RelayCommand SaveQueueAsPlaylistCommand { get; private set; } = null!;
    public RelayCommand RenamePlaylistCommand { get; private set; } = null!;
    public RelayCommand DuplicatePlaylistCommand { get; private set; } = null!;
    public RelayCommand DeletePlaylistCommand { get; private set; } = null!;
    public RelayCommand LoadPlaylistCommand { get; private set; } = null!;
    public RelayCommand PlayPlaylistCommand { get; private set; } = null!;
    public RelayCommand AddQueueToPlaylistCommand { get; private set; } = null!;
    public RelayCommand ExportSelectedPlaylistCommand { get; private set; } = null!;
    public RelayCommand<Track> AddLibraryTrackToPlaylistCommand { get; private set; } = null!;
    public RelayCommand<LibraryFolderNode> AddLibraryFolderToPlaylistCommand { get; private set; } = null!;
    public RelayCommand<Track> RemovePlaylistTrackCommand { get; private set; } = null!;
    public RelayCommand<Track> MoveQueueTrackUpCommand { get; private set; } = null!;
    public RelayCommand<Track> MoveQueueTrackDownCommand { get; private set; } = null!;
    public RelayCommand<Track> MovePlaylistTrackUpCommand { get; private set; } = null!;
    public RelayCommand<Track> MovePlaylistTrackDownCommand { get; private set; } = null!;

    public string QueueSearchText
    {
        get => _queueSearchText;
        set
        {
            if (SetProperty(ref _queueSearchText, value ?? string.Empty))
            {
                RefreshQueueView();
            }
        }
    }

    public string QueueVisibleCountText
    {
        get
        {
            var count = QueueView?.Cast<object>().Count() ?? Queue.Count;
            return string.IsNullOrWhiteSpace(QueueSearchText)
                ? QueueCountText
                : $"{count} of {QueueCountText}";
        }
    }

    public string PlaylistNameText
    {
        get => _playlistNameText;
        set => SetProperty(ref _playlistNameText, value ?? string.Empty);
    }

    public string PlaylistSearchText
    {
        get => _playlistSearchText;
        set
        {
            if (SetProperty(ref _playlistSearchText, value ?? string.Empty))
            {
                PlaylistsView.Refresh();
                OnPropertyChanged(nameof(PlaylistVisibleCountText));
            }
        }
    }

    public string PlaylistVisibleCountText
    {
        get
        {
            var count = PlaylistsView?.Cast<object>().Count() ?? Playlists.Count;
            return count == 1 ? "1 playlist" : $"{count} playlists";
        }
    }

    public SavedPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            if (SetProperty(ref _selectedPlaylist, value))
            {
                PlaylistNameText = value?.Name ?? string.Empty;
                RebuildSelectedPlaylistTracks();
                OnPropertyChanged(nameof(SelectedPlaylistName));
                OnPropertyChanged(nameof(SelectedPlaylistCountText));
                OnPropertyChanged(nameof(HasSelectedPlaylist));
            }
        }
    }

    public Track? SelectedPlaylistTrack
    {
        get => _selectedPlaylistTrack;
        set => SetProperty(ref _selectedPlaylistTrack, value);
    }

    public string SelectedPlaylistName => SelectedPlaylist?.Name ?? "No playlist selected";
    public string SelectedPlaylistCountText => SelectedPlaylist?.TrackCountText ?? "Select or create a playlist";
    public bool HasSelectedPlaylist => SelectedPlaylist is not null;

    private void InitializeQueueAndPlaylistState()
    {
        Playlists = [];
        SelectedPlaylistTracks = [];

        QueueView = CollectionViewSource.GetDefaultView(Queue);
        QueueView.Filter = QueueFilter;

        PlaylistsView = CollectionViewSource.GetDefaultView(Playlists);
        PlaylistsView.Filter = PlaylistFilter;
        PlaylistsView.SortDescriptions.Add(new SortDescription(nameof(SavedPlaylist.Name), ListSortDirection.Ascending));
    }

    private void InitializeQueueAndPlaylistCommands()
    {
        CreatePlaylistCommand = new RelayCommand(CreatePlaylist);
        SaveQueueAsPlaylistCommand = new RelayCommand(SaveQueueAsPlaylist);
        RenamePlaylistCommand = new RelayCommand(() => _ = RenameSelectedPlaylistAsync(PlaylistNameText));
        DuplicatePlaylistCommand = new RelayCommand(DuplicateSelectedPlaylist);
        DeletePlaylistCommand = new RelayCommand(DeleteSelectedPlaylist);
        LoadPlaylistCommand = new RelayCommand(() => _ = LoadSelectedPlaylistAsync(playFirst: false));
        PlayPlaylistCommand = new RelayCommand(() => _ = LoadSelectedPlaylistAsync(playFirst: true));
        AddQueueToPlaylistCommand = new RelayCommand(AddQueueToSelectedPlaylist);
        ExportSelectedPlaylistCommand = new RelayCommand(() => _ = ExportSelectedPlaylistAsync());
        AddLibraryTrackToPlaylistCommand = new RelayCommand<Track>(AddLibraryTrackToSelectedPlaylist);
        AddLibraryFolderToPlaylistCommand = new RelayCommand<LibraryFolderNode>(folder =>
            AddTracksToSelectedPlaylist(folder?.DescendantTracks() ?? []));
        RemovePlaylistTrackCommand = new RelayCommand<Track>(RemoveTrackFromSelectedPlaylist);
        MoveQueueTrackUpCommand = new RelayCommand<Track>(track => MoveQueueTrackByOffset(track, -1));
        MoveQueueTrackDownCommand = new RelayCommand<Track>(track => MoveQueueTrackByOffset(track, 1));
        MovePlaylistTrackUpCommand = new RelayCommand<Track>(track => MovePlaylistTrackByOffset(track, -1));
        MovePlaylistTrackDownCommand = new RelayCommand<Track>(track => MovePlaylistTrackByOffset(track, 1));
    }

    public async Task InitializePlaylistsAsync()
    {
        try
        {
            var loaded = await _playlistService.LoadSavedAsync().ConfigureAwait(false);
            RunOnUiThread(() =>
            {
                Playlists.Clear();
                foreach (var playlist in loaded)
                {
                    Playlists.Add(playlist);
                }

                PlaylistsView.Refresh();
                _playlistsInitialized = true;
                SelectedPlaylist = _settings.SelectedPlaylistId is { } selectedId
                    ? Playlists.FirstOrDefault(playlist => playlist.Id == selectedId) ?? Playlists.FirstOrDefault()
                    : Playlists.FirstOrDefault();
                OnPropertyChanged(nameof(PlaylistVisibleCountText));
                StatusText = Playlists.Count == 0
                    ? StatusText
                    : $"Playlists ready • {PlaylistVisibleCountText}";
            });
        }
        catch (Exception exception)
        {
            _logger.Error("Playlist initialization failed.", exception);
            RunOnUiThread(() => StatusText = "Playlists could not be loaded • Existing playback remains available");
        }
    }

    public void MoveQueueTrack(Track? source, Track? target)
    {
        if (source is null || target is null || ReferenceEquals(source, target))
        {
            return;
        }

        var oldIndex = FindQueueIndex(source.FilePath);
        var newIndex = FindQueueIndex(target.FilePath);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        Queue.Move(oldIndex, newIndex);
        SelectedQueueTrack = source;
        OnQueueChanged();
        StatusText = $"Moved {source.Title} to position {newIndex + 1}.";
    }

    public void MoveQueueTrackToEnd(Track? source)
    {
        if (source is null || Queue.Count < 2)
        {
            return;
        }

        var oldIndex = FindQueueIndex(source.FilePath);
        var newIndex = Queue.Count - 1;
        if (oldIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        Queue.Move(oldIndex, newIndex);
        SelectedQueueTrack = source;
        OnQueueChanged();
        StatusText = $"Moved {source.Title} to the end of the queue.";
    }

    private bool QueueFilter(object item)
    {
        if (item is not Track track || string.IsNullOrWhiteSpace(QueueSearchText))
        {
            return true;
        }

        var query = QueueSearchText.Trim();
        return ContainsIgnoreCase(track.Title, query) ||
               ContainsIgnoreCase(track.Artist, query) ||
               ContainsIgnoreCase(track.Album, query) ||
               ContainsIgnoreCase(track.FilePath, query);
    }

    private bool PlaylistFilter(object item)
    {
        if (item is not SavedPlaylist playlist || string.IsNullOrWhiteSpace(PlaylistSearchText))
        {
            return true;
        }

        return playlist.Name.Contains(PlaylistSearchText.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private void RefreshQueueView()
    {
        QueueView?.Refresh();
        OnPropertyChanged(nameof(QueueVisibleCountText));
    }

    private void CreatePlaylist()
    {
        var name = MakeUniquePlaylistName(string.IsNullOrWhiteSpace(PlaylistNameText)
            ? $"Playlist {DateTime.Now:yyyy-MM-dd HHmm}"
            : PlaylistNameText.Trim());

        var playlist = new SavedPlaylist { Name = name };
        Playlists.Add(playlist);
        PlaylistsView.Refresh();
        SelectedPlaylist = playlist;
        OnPropertyChanged(nameof(PlaylistVisibleCountText));
        _ = PersistPlaylistsAsync();
        StatusText = $"Created playlist {name}.";
    }

    private void SaveQueueAsPlaylist()
    {
        if (Queue.Count == 0)
        {
            StatusText = "Add music to the queue before saving a playlist.";
            return;
        }

        var name = MakeUniquePlaylistName(string.IsNullOrWhiteSpace(PlaylistNameText)
            ? $"Queue {DateTime.Now:yyyy-MM-dd HHmm}"
            : PlaylistNameText.Trim());

        var playlist = new SavedPlaylist
        {
            Name = name,
            TrackPaths = Queue.Select(track => track.FilePath).ToList()
        };

        Playlists.Add(playlist);
        PlaylistsView.Refresh();
        SelectedPlaylist = playlist;
        OnPropertyChanged(nameof(PlaylistVisibleCountText));
        _ = PersistPlaylistsAsync();
        StatusText = $"Saved {QueueCountText.ToLowerInvariant()} as {name}.";
    }

    public async Task<bool> RenameSelectedPlaylistAsync(string? requestedName)
    {
        if (SelectedPlaylist is null)
        {
            StatusText = "Select a playlist before renaming it.";
            return false;
        }

        var normalizedName = requestedName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            StatusText = "Enter a playlist name first.";
            return false;
        }

        var playlist = SelectedPlaylist;
        var finalName = MakeUniquePlaylistName(normalizedName, playlist.Id);
        if (string.Equals(playlist.Name, finalName, StringComparison.Ordinal))
        {
            PlaylistNameText = finalName;
            StatusText = $"Playlist is already named {finalName}.";
            return true;
        }

        playlist.Name = finalName;
        playlist.UpdatedUtc = DateTimeOffset.UtcNow;
        PlaylistNameText = finalName;

        // Refresh both the sorted playlist view and all selected-playlist labels.
        // This also repositions the renamed item alphabetically without losing it.
        PlaylistsView.Refresh();
        PlaylistsView.MoveCurrentTo(playlist);
        OnPropertyChanged(nameof(SelectedPlaylistName));
        OnPropertyChanged(nameof(PlaylistVisibleCountText));

        await PersistPlaylistsAsync().ConfigureAwait(true);
        StatusText = $"Playlist renamed to {finalName}.";
        return true;
    }

    private void DuplicateSelectedPlaylist()
    {
        if (SelectedPlaylist is null)
        {
            StatusText = "Select a playlist before duplicating it.";
            return;
        }

        var duplicate = SelectedPlaylist.Clone(MakeUniquePlaylistName(SelectedPlaylist.Name + " Copy"));
        Playlists.Add(duplicate);
        PlaylistsView.Refresh();
        SelectedPlaylist = duplicate;
        OnPropertyChanged(nameof(PlaylistVisibleCountText));
        _ = PersistPlaylistsAsync();
        StatusText = $"Duplicated playlist as {duplicate.Name}.";
    }

    private void DeleteSelectedPlaylist()
    {
        if (SelectedPlaylist is null)
        {
            StatusText = "Select a playlist before deleting it.";
            return;
        }

        var deletedName = SelectedPlaylist.Name;
        var index = Playlists.IndexOf(SelectedPlaylist);
        Playlists.Remove(SelectedPlaylist);
        SelectedPlaylist = Playlists.Count == 0 ? null : Playlists[Math.Clamp(index, 0, Playlists.Count - 1)];
        PlaylistsView.Refresh();
        OnPropertyChanged(nameof(PlaylistVisibleCountText));
        _ = PersistPlaylistsAsync();
        StatusText = $"Deleted playlist {deletedName}.";
    }

    private async Task LoadSelectedPlaylistAsync(bool playFirst)
    {
        if (SelectedPlaylist is null)
        {
            StatusText = "Select a playlist first.";
            return;
        }

        var tracks = SelectedPlaylist.TrackPaths
            .Where(File.Exists)
            .Select(ResolvePlaylistTrack)
            .ToArray();

        if (tracks.Length == 0)
        {
            StatusText = "That playlist has no available tracks.";
            return;
        }

        await AddLibraryTracksAsync(tracks, playFirst, replaceQueue: true);
        StatusText = playFirst
            ? $"Playing {SelectedPlaylist.Name}."
            : $"Loaded {SelectedPlaylist.Name} into the queue.";
    }

    private void AddQueueToSelectedPlaylist()
    {
        if (Queue.Count == 0)
        {
            StatusText = "The queue is empty.";
            return;
        }

        AddPathsToSelectedPlaylist(Queue.Select(track => track.FilePath));
    }

    private void AddLibraryTrackToSelectedPlaylist(Track? track)
    {
        if (track is null)
        {
            return;
        }

        AddPathsToSelectedPlaylist([track.FilePath]);
    }

    private void AddTracksToSelectedPlaylist(IEnumerable<Track> tracks) =>
        AddPathsToSelectedPlaylist(tracks.Select(track => track.FilePath));

    private void AddPathsToSelectedPlaylist(IEnumerable<string> paths)
    {
        if (SelectedPlaylist is null)
        {
            StatusText = "Select or create a playlist first.";
            return;
        }

        var existing = new HashSet<string>(SelectedPlaylist.TrackPaths, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
        {
            if (existing.Add(path))
            {
                SelectedPlaylist.TrackPaths.Add(path);
                added++;
            }
        }

        if (added == 0)
        {
            StatusText = "Those tracks are already in the selected playlist.";
            return;
        }

        SelectedPlaylist.UpdatedUtc = DateTimeOffset.UtcNow;
        SelectedPlaylist.NotifyTracksChanged();
        RebuildSelectedPlaylistTracks();
        PlaylistsView.Refresh();
        _ = PersistPlaylistsAsync();
        StatusText = $"Added {added} {(added == 1 ? "track" : "tracks")} to {SelectedPlaylist.Name}.";
    }

    private void RemoveTrackFromSelectedPlaylist(Track? track)
    {
        if (SelectedPlaylist is null || track is null)
        {
            return;
        }

        var index = SelectedPlaylist.TrackPaths.FindIndex(path => PathsEqual(path, track.FilePath));
        if (index < 0)
        {
            return;
        }

        SelectedPlaylist.TrackPaths.RemoveAt(index);
        SelectedPlaylist.UpdatedUtc = DateTimeOffset.UtcNow;
        SelectedPlaylist.NotifyTracksChanged();
        RebuildSelectedPlaylistTracks();
        PlaylistsView.Refresh();
        _ = PersistPlaylistsAsync();
        StatusText = $"Removed {track.Title} from {SelectedPlaylist.Name}.";
    }

    private void MoveQueueTrackByOffset(Track? track, int offset)
    {
        if (track is null)
        {
            return;
        }

        var index = FindQueueIndex(track.FilePath);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Queue.Count)
        {
            return;
        }

        Queue.Move(index, target);
        SelectedQueueTrack = track;
        OnQueueChanged();
        StatusText = $"Moved {track.Title} to position {target + 1}.";
    }

    private void MovePlaylistTrackByOffset(Track? track, int offset)
    {
        if (SelectedPlaylist is null || track is null)
        {
            return;
        }

        var index = SelectedPlaylist.TrackPaths.FindIndex(path => PathsEqual(path, track.FilePath));
        var target = index + offset;
        if (index < 0 || target < 0 || target >= SelectedPlaylist.TrackPaths.Count)
        {
            return;
        }

        (SelectedPlaylist.TrackPaths[index], SelectedPlaylist.TrackPaths[target]) =
            (SelectedPlaylist.TrackPaths[target], SelectedPlaylist.TrackPaths[index]);
        SelectedPlaylist.UpdatedUtc = DateTimeOffset.UtcNow;
        SelectedPlaylist.NotifyTracksChanged();
        RebuildSelectedPlaylistTracks();
        SelectedPlaylistTrack = SelectedPlaylistTracks.ElementAtOrDefault(target);
        _ = PersistPlaylistsAsync();
        StatusText = $"Reordered {track.Title} in {SelectedPlaylist.Name}.";
    }

    private void RebuildSelectedPlaylistTracks()
    {
        SelectedPlaylistTracks.Clear();
        if (SelectedPlaylist is null)
        {
            SelectedPlaylistTrack = null;
            return;
        }

        foreach (var path in SelectedPlaylist.TrackPaths)
        {
            SelectedPlaylistTracks.Add(ResolvePlaylistTrack(path));
        }

        SelectedPlaylistTrack = SelectedPlaylistTracks.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedPlaylistCountText));
    }

    private Track ResolvePlaylistTrack(string path)
    {
        var indexed = _libraryTracks.FirstOrDefault(track => PathsEqual(track.FilePath, path));
        return indexed ?? Track.FromPath(path);
    }

    private async Task ImportPlaylistAsync()
    {
        var path = _filePicker.PickPlaylistFile();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "Playlist import cancelled.";
            return;
        }

        try
        {
            var imported = await _playlistService.ImportAsync(path).ConfigureAwait(false);
            RunOnUiThread(() =>
            {
                imported.Name = MakeUniquePlaylistName(imported.Name);
                Playlists.Add(imported);
                PlaylistsView.Refresh();
                SelectedPlaylist = imported;
                OnPropertyChanged(nameof(PlaylistVisibleCountText));
                _ = PersistPlaylistsAsync();
                StatusText = $"Imported {imported.Name} • {imported.TrackCountText}.";
                if (IsQueueActive)
                {
                    _ = LoadSelectedPlaylistAsync(playFirst: false);
                }
            });
        }
        catch (Exception exception)
        {
            _logger.Error($"Playlist import failed: {path}", exception);
            RunOnUiThread(() => StatusText = $"Playlist import failed: {exception.Message}");
        }
    }

    private async Task ExportSelectedPlaylistAsync()
    {
        if (SelectedPlaylist is null)
        {
            StatusText = "Select a playlist before exporting it.";
            return;
        }

        var path = _filePicker.PickPlaylistSavePath(SelectedPlaylist.Name);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "Playlist export cancelled.";
            return;
        }

        try
        {
            await _playlistService.ExportAsync(path, SelectedPlaylist).ConfigureAwait(false);
            RunOnUiThread(() => StatusText = $"Exported {SelectedPlaylist.Name} to {Path.GetFileName(path)}.");
        }
        catch (Exception exception)
        {
            _logger.Error($"Playlist export failed: {path}", exception);
            RunOnUiThread(() => StatusText = $"Playlist export failed: {exception.Message}");
        }
    }

    private async Task ExportQueueAsync()
    {
        if (Queue.Count == 0)
        {
            StatusText = "The queue is empty.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(PlaylistNameText) ? "Cadence queue" : PlaylistNameText.Trim();
        var path = _filePicker.PickPlaylistSavePath(name);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "Queue export cancelled.";
            return;
        }

        var playlist = new SavedPlaylist
        {
            Name = name,
            TrackPaths = Queue.Select(track => track.FilePath).ToList()
        };

        try
        {
            await _playlistService.ExportAsync(path, playlist).ConfigureAwait(false);
            RunOnUiThread(() => StatusText = $"Exported queue to {Path.GetFileName(path)}.");
        }
        catch (Exception exception)
        {
            _logger.Error($"Queue export failed: {path}", exception);
            RunOnUiThread(() => StatusText = $"Queue export failed: {exception.Message}");
        }
    }

    private async Task PersistPlaylistsAsync()
    {
        try
        {
            await _playlistService.SaveSavedAsync(Playlists).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Error("Playlists could not be saved.", exception);
            RunOnUiThread(() => StatusText = "Playlist change could not be saved • See the application log");
        }
    }

    private string MakeUniquePlaylistName(string requestedName, Guid? ignoredId = null)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? "Untitled playlist" : requestedName.Trim();
        var name = baseName;
        var suffix = 2;
        while (Playlists.Any(playlist =>
                   playlist.Id != ignoredId &&
                   string.Equals(playlist.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            name = $"{baseName} ({suffix++})";
        }

        return name;
    }
}

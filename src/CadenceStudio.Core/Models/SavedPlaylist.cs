using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CadenceStudio.Core.Models;

public sealed class SavedPlaylist : INotifyPropertyChanged
{
    private string _name = "Untitled playlist";
    private List<string> _trackPaths = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Untitled playlist" : value.Trim();
            if (string.Equals(_name, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _name = normalized;
            OnPropertyChanged();
        }
    }

    public List<string> TrackPaths
    {
        get => _trackPaths;
        set
        {
            _trackPaths = value ?? [];
            OnPropertyChanged();
            OnPropertyChanged(nameof(TrackCountText));
        }
    }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string TrackCountText => TrackPaths.Count == 1 ? "1 track" : $"{TrackPaths.Count} tracks";

    public void NotifyTracksChanged()
    {
        OnPropertyChanged(nameof(TrackPaths));
        OnPropertyChanged(nameof(TrackCountText));
    }

    public void Normalize()
    {
        if (Id == Guid.Empty)
        {
            Id = Guid.NewGuid();
        }

        Name = string.IsNullOrWhiteSpace(Name) ? "Untitled playlist" : Name.Trim();
        TrackPaths = TrackPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .ToList();

        if (CreatedUtc == default)
        {
            CreatedUtc = DateTimeOffset.UtcNow;
        }

        if (UpdatedUtc == default)
        {
            UpdatedUtc = CreatedUtc;
        }
    }

    public SavedPlaylist Clone(string? name = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim(),
        TrackPaths = [.. TrackPaths],
        CreatedUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

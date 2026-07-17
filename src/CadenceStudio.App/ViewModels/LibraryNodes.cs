using System.Collections.ObjectModel;
using CadenceStudio.Core.Models;

namespace CadenceStudio.App.ViewModels;

/// <summary>
/// Represents one real folder in the indexed music library. Children contain
/// additional folders followed by tracks, preserving the on-disk hierarchy.
/// </summary>
public sealed class LibraryFolderNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public ObservableCollection<object> Children { get; } = [];
    public int TrackCount { get; init; }
    public int FolderCount { get; init; }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (FolderCount > 0)
            {
                parts.Add($"{FolderCount} {(FolderCount == 1 ? "folder" : "folders")}");
            }

            parts.Add($"{TrackCount} {(TrackCount == 1 ? "track" : "tracks")}");
            return string.Join(" • ", parts);
        }
    }

    public IEnumerable<Track> DescendantTracks()
    {
        foreach (var child in Children)
        {
            if (child is Track track)
            {
                yield return track;
            }
            else if (child is LibraryFolderNode folder)
            {
                foreach (var descendant in folder.DescendantTracks())
                {
                    yield return descendant;
                }
            }
        }
    }
}

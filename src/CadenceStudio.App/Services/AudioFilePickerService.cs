using System.IO;
using CadenceStudio.Core.Contracts;
using Microsoft.Win32;

namespace CadenceStudio.App.Services;

public sealed class AudioFilePickerService : IFilePickerService
{
    public IReadOnlyList<string> PickAudioFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open music in Cadence Studio",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true,
            Filter = "Audio files|*.mp3;*.wav;*.aif;*.aiff;*.flac;*.m4a;*.aac;*.wma|MP3 files|*.mp3|FLAC files|*.flac|WAV files|*.wav|All files|*.*"
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }

    public string? PickPlaylistFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import playlist into Cadence Studio",
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true,
            Filter = "M3U playlists|*.m3u;*.m3u8|M3U8 playlists|*.m3u8|M3U playlists|*.m3u|All files|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickPlaylistSavePath(string suggestedName)
    {
        var safeName = string.Join("_", (suggestedName ?? "Cadence playlist")
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        var dialog = new SaveFileDialog
        {
            Title = "Export Cadence Studio playlist",
            AddExtension = true,
            DefaultExt = ".m3u8",
            FileName = string.IsNullOrWhiteSpace(safeName) ? "Cadence playlist.m3u8" : safeName + ".m3u8",
            Filter = "M3U8 playlists|*.m3u8|M3U playlists|*.m3u"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

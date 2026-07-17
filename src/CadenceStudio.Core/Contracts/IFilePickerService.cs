namespace CadenceStudio.Core.Contracts;

public interface IFilePickerService
{
    IReadOnlyList<string> PickAudioFiles();
    string? PickPlaylistFile();
    string? PickPlaylistSavePath(string suggestedName);
}

namespace CadenceStudio.Core.Models;

public sealed class MusicBrainzReleaseCandidate
{
    public string RecordingId { get; set; } = string.Empty;
    public string RecordingTitle { get; set; } = string.Empty;
    public string ArtistId { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public int RecordingScore { get; set; }
    public string ReleaseId { get; set; } = string.Empty;
    public string ReleaseGroupId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public int? TrackCount { get; set; }
    public bool HasFrontCover { get; set; }
    public int RankingScore { get; set; }

    public string DisplayLabel
    {
        get
        {
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(Date)) details.Add(Date);
            if (!string.IsNullOrWhiteSpace(Country)) details.Add(Country);
            if (!string.IsNullOrWhiteSpace(Format)) details.Add(Format);
            if (TrackCount is > 0) details.Add($"{TrackCount} tracks");
            if (!string.IsNullOrWhiteSpace(Status)) details.Add(Status);
            return details.Count == 0 ? Title : $"{Title} — {string.Join(" • ", details)}";
        }
    }
}

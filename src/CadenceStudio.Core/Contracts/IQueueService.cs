using CadenceStudio.Core.Models;

namespace CadenceStudio.Core.Contracts;

public interface IQueueService
{
    IReadOnlyList<Track> Items { get; }
    int CurrentIndex { get; }
    event EventHandler? QueueChanged;

    bool Add(Track track);
    bool Remove(Track track);
    void Clear();
    void Move(int oldIndex, int newIndex);
    Track? GetCurrent();
    Track? MoveNext();
    Track? MovePrevious();
}

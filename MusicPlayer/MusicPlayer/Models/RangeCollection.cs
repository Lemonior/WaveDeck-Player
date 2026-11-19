using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MusicPlayer.Models;

public sealed class RangeCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        CheckReentrancy(); var added = false;
        foreach (var item in items) { Items.Add(item); added = true; }
        if (!added) return;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

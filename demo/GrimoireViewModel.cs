using System;
using System.ComponentModel;

namespace NecronomiDomDemo;

internal sealed class GrimoireViewModel : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs CountTextChanged = new(nameof(CountText));
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count { get; private set; }
    public string CountText => Count == 0
        ? "Nothing stirs."
        : Count == 1 ? "One shape stirs beneath the page." : Count + " shapes stir beneath the page.";

    public void Summon() => SetCount(Count + 1);
    public void Banish() => SetCount(0);

    private void SetCount(int count)
    {
        count = Math.Max(0, count);
        if (Count == count) return;
        Count = count;
        PropertyChanged?.Invoke(this, CountChanged);
        PropertyChanged?.Invoke(this, CountTextChanged);
    }
}

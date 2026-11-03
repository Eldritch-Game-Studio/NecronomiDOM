using System;
using System.ComponentModel;

namespace NecronomiDomDemo;

/// <summary>Demo-owned state, deliberately independent of the DOM and Godot.</summary>
internal sealed class WorkbenchViewModel : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs AllChanged = new(null);
    public event PropertyChangedEventHandler? PropertyChanged;
    public string DraftName { get; private set; } = "Aster Vale";
    public string DraftMotto { get; private set; } = "Leave a light in the dark.";
    public string SavedName { get; private set; } = "Aster Vale";
    public bool LanternEquipped { get; private set; }
    private bool _savedLantern;
    private string _savedMotto = "Leave a light in the dark.";
    private int _activeQuest = -1;
    private int _progress;
    private readonly bool[] _completed = new bool[3];
    private static readonly string[] Titles = { "Lights in the marsh", "The missing pages", "A remedy for rain" };
    private static readonly string[] Findings = { "lanterns lit", "pages recovered", "herbs gathered" };
    public int Supplies { get; private set; }
    public bool DeveloperAvailable { get; }
    public bool Watching { get; private set; }
    public bool ReferenceVisible { get; private set; }
    public string ReloadStatus { get; private set; } = "Ready · manual reload only. Source watching is off.";
    public bool ReloadFailed { get; private set; }
    public string ProfileStatus { get; private set; } = "Profile saved. Make this adventurer yours.";
    public string CharacterPreview => string.IsNullOrWhiteSpace(DraftName) ? "An unnamed adventurer" : DraftName;
    public string LoadoutText => LanternEquipped ? "Field lantern equipped" : "Travel satchel only";
    public string EquipLabel => LanternEquipped ? "Stow lantern" : "Equip lantern";
    public bool CanSave => !string.IsNullOrWhiteSpace(DraftName) && IsEditing;
    public bool IsEditing => DraftName != SavedName || DraftMotto != _savedMotto || LanternEquipped != _savedLantern;
    public bool HasQuest => _activeQuest >= 0;
    public bool NoQuest => !HasQuest;
    public bool CanRecord => HasQuest && _progress < 3;
    public bool CanClaim => HasQuest && _progress == 3;
    public string TrackerTitle => HasQuest ? Titles[_activeQuest] : "No active quest";
    public string TrackerProgress => HasQuest ? $"{_progress} / 3 {Findings[_activeQuest]}" : "Choose a contract from the board.";
    public string TrackerHint => CanClaim ? "All findings recorded. Collect your supplies." : "Use Record finding to simulate this local quest's next step.";
    public string SupplyText => $"{Supplies} supplies earned";
    public string WatchLabel => Watching ? "Watching: ON" : "Watch sources: OFF";
    public bool CanAccept(int quest) => !HasQuest && !_completed[quest];
    public string QuestLabel(int quest) => _completed[quest] ? "Completed" : _activeQuest == quest ? "Tracking" : HasQuest ? "One quest at a time" : "Accept quest";

    public WorkbenchViewModel(bool developerAvailable) => DeveloperAvailable = developerAvailable;
    private void Changed() => PropertyChanged?.Invoke(this, AllChanged);
    public void EditName(string value) { if (DraftName == value) return; DraftName = value; Changed(); }
    public void EditMotto(string value) { if (DraftMotto == value) return; DraftMotto = value; Changed(); }
    public void ToggleLantern() { LanternEquipped = !LanternEquipped; Changed(); }
    public void SaveProfile()
    {
        if (!CanSave) return;
        SavedName = DraftName;
        _savedMotto = DraftMotto;
        _savedLantern = LanternEquipped;
        ProfileStatus = $"Saved profile for {SavedName}.";
        Changed();
    }
    public void Accept(int quest)
    {
        if (!CanAccept(quest)) return;
        _activeQuest = quest;
        _progress = 0;
        Changed();
    }
    public void RecordFinding() { if (!CanRecord) return; _progress++; Changed(); }
    public void Claim()
    {
        if (!CanClaim) return;
        _completed[_activeQuest] = true;
        Supplies += 12;
        _activeQuest = -1;
        _progress = 0;
        Changed();
    }
    public void SetWatching(bool watching) { Watching = watching; Changed(); }
    public void ToggleReference() { ReferenceVisible = !ReferenceVisible; Changed(); }
    public void ShowReload(string status, bool failed = false) { ReloadStatus = status; ReloadFailed = failed; Changed(); }
}

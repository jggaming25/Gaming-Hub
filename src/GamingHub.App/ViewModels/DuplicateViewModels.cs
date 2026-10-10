using CommunityToolkit.Mvvm.ComponentModel;
using GamingHub.Core.Models;

namespace GamingHub.App.ViewModels;

public partial class DuplicateFileViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isSelected;

    public DuplicateFile Model { get; }
    public bool IsKeepCandidate { get; set; }

    public DuplicateFileViewModel(DuplicateFile model) => Model = model;

    public string FileName => Model.FileName;
    public string Directory => Model.Directory;
    public string Path => Model.Path;
    public string SizeDisplay => Format.Bytes(Model.Size);
    public string DateDisplay => Model.LastWriteUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}

public partial class DuplicateGroupViewModel : ViewModelBase
{
    private readonly DuplicateGroup _group;

    public System.Collections.ObjectModel.ObservableCollection<DuplicateFileViewModel> Files { get; } = new();

    public DuplicateGroupViewModel(DuplicateGroup group)
    {
        _group = group;
        // Keep the oldest file as the original, delete the newer copies.
        var ordered = group.Files.OrderBy(f => f.LastWriteUtc).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var vm = new DuplicateFileViewModel(ordered[i]) { IsKeepCandidate = i == 0 };
            Files.Add(vm);
        }
    }

    public string Hash => _group.Hash;
    public int CopyCount => Files.Count;
    public long FileSize => _group.FileSize;
    public string SizeDisplay => Format.Bytes(_group.FileSize);
    public string WastedDisplay => Format.Bytes(_group.WastedBytes);
    public string Header => $"{Files.Count}× · {Files[0].FileName} · {SizeDisplay} je Datei";
    public string ReclaimText => $"Freigebbar: {WastedDisplay}";

    public IEnumerable<string> SelectedPaths => Files.Where(f => f.IsSelected).Select(f => f.Path);
    public IEnumerable<DuplicateFileViewModel> SelectedFiles => Files.Where(f => f.IsSelected);

    public void SelectAllButOne()
    {
        bool keepTaken = false;
        foreach (var f in Files)
        {
            if (f.IsKeepCandidate && !keepTaken)
            {
                keepTaken = true;
                f.IsSelected = false;
            }
            else
            {
                f.IsSelected = true;
            }
        }
    }

    public void SelectNone()
    {
        foreach (var f in Files)
            f.IsSelected = false;
    }
}

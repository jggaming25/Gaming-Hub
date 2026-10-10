using CommunityToolkit.Mvvm.ComponentModel;

namespace GamingHub.App.ViewModels;

/// <summary>Selectable drive for cleanup scanning.</summary>
public partial class DriveScanOption : ObservableObject
{
    public DriveScanOption(string root, string label)
    {
        Root = root;
        Label = label;
    }

    public string Root { get; }
    public string Label { get; }
    public string Display => string.IsNullOrWhiteSpace(Label) ? Root : $"{Root} ({Label})";

    [ObservableProperty] private bool _isSelected;
}
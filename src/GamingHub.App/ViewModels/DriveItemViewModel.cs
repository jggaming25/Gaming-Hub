using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.Core.Models;

namespace GamingHub.App.ViewModels;

/// <summary>
/// Wraps a <see cref="DiskVolume"/> and lets the user switch the displayed
/// unit between GB (Standard) und MB by clicking the unit label.
/// </summary>
public partial class DriveItemViewModel : ViewModelBase
{
    private readonly DiskVolume _model;

    public DriveItemViewModel(DiskVolume model, bool megabytes = false)
    {
        _model = model;
        _isMegabytes = megabytes;
    }

    public string Root => _model.Root;
    public string Label => _model.Label;
    public double UsedPercent => _model.UsedPercent;

    [ObservableProperty] private bool _isMegabytes;

    public string UnitLabel => IsMegabytes ? "MB" : "GB";
    public string FreeValue => FormatNumber(_model.FreeBytes);
    public string TotalValue => FormatNumber(_model.TotalBytes);
    public string UsedValue => FormatNumber(_model.UsedBytes);

    partial void OnIsMegabytesChanged(bool value)
    {
        OnPropertyChanged(nameof(UnitLabel));
        OnPropertyChanged(nameof(FreeValue));
        OnPropertyChanged(nameof(TotalValue));
        OnPropertyChanged(nameof(UsedValue));
    }

    [RelayCommand]
    private void ToggleUnit() => IsMegabytes = !IsMegabytes;

    private string FormatNumber(long bytes)
    {
        double value;
        if (IsMegabytes)
            value = bytes / 1024.0 / 1024.0;
        else
            value = bytes / 1024.0 / 1024.0 / 1024.0;

        return IsMegabytes ? value.ToString("N0") : value.ToString("N2");
    }
}

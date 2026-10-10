using GamingHub.Core.Models;

namespace GamingHub.App.ViewModels;

/// <summary>
/// Wraps a <see cref="DiskVolume"/>. Sizes are always displayed in GB
/// (fixed – no unit switching).
/// </summary>
public sealed class DriveItemViewModel
{
    private readonly DiskVolume _model;

    public DriveItemViewModel(DiskVolume model)
    {
        _model = model;
    }

    public string Root => _model.Root;
    public string Label => _model.Label;
    public double UsedPercent => _model.UsedPercent;
    public string UnitLabel => "GB";
    public string FreeValue => FormatGb(_model.FreeBytes);
    public string TotalValue => FormatGb(_model.TotalBytes);
    public string UsedValue => FormatGb(_model.UsedBytes);

    private static string FormatGb(long bytes)
        => (bytes / 1024.0 / 1024.0 / 1024.0).ToString("N2");
}
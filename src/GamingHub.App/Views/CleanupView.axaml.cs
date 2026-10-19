using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GamingHub.App.ViewModels;

namespace GamingHub.App.Views;

public partial class CleanupView : UserControl
{
    public CleanupView()
    {
        InitializeComponent();
    }

    private async void AddFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CleanupViewModel vm)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
            return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Suchordner auswählen",
            AllowMultiple = true
        });

        foreach (var folder in folders)
        {
            string path = folder.Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(path))
                vm.AddRoot(path);
        }
    }

    private void RemoveRootClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CleanupViewModel vm && sender is Button { DataContext: string path })
            vm.RemoveRootCommand.Execute(path);
    }

    private async void ExportReportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CleanupViewModel vm)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
            return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Disk-Cleanup-Bericht speichern",
            SuggestedFileName = $"gaminghub-cleanup-{DateTime.Now:yyyy-MM-dd-HHmm}.txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Textdatei") { Patterns = new[] { "*.txt" } }
            }
        });

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
        await writer.WriteAsync(vm.BuildReport());
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GamingHub.App.ViewModels;

namespace GamingHub.App.Views;

public partial class ChatView : UserControl
{
    private ChatViewModel? _vm;

    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ChatViewModel);
    }

    private void Attach(ChatViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm is not null)
        {
            _vm.MessageScrolled -= ScrollToEnd;
            _vm.PickFileRequested = null;
        }
        _vm = vm;
        if (_vm is not null)
        {
            _vm.MessageScrolled += ScrollToEnd;
            _vm.PickFileRequested = PickFileAsync;
        }
    }

    private async Task<string?> PickFileAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Datei zum Senden auswählen",
            AllowMultiple = false
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private void ScrollToEnd()
    {
        Dispatcher.UIThread.Post(() =>
        {
            MessageScroller.Offset = new Vector(MessageScroller.Offset.X, double.MaxValue);
        }, DispatcherPriority.Background);
    }
}

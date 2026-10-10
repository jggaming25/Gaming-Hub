using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
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
            _vm.MessageScrolled -= ScrollToEnd;
        _vm = vm;
        if (_vm is not null)
            _vm.MessageScrolled += ScrollToEnd;
    }

    private void ScrollToEnd()
    {
        Dispatcher.UIThread.Post(() =>
        {
            MessageScroller.Offset = new Vector(MessageScroller.Offset.X, double.MaxValue);
        }, DispatcherPriority.Background);
    }
}

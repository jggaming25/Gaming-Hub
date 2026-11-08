using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GamingHub.App.Behaviors;

/// <summary>
/// Scrollt einen <see cref="ScrollViewer"/> automatisch an das rechte Ende,
/// sobald sich der gebundene Token ändert oder sich der Inhalt vergrößert.
/// Damit stehen Diagramme nach jedem Neuladen direkt auf dem neuesten Stand.
/// </summary>
public sealed class AutoScroll
{
    public static readonly AttachedProperty<int> ToEndTokenProperty =
        AvaloniaProperty.RegisterAttached<AutoScroll, ScrollViewer, int>("ToEndToken");

    private AutoScroll()
    {
    }

    private static readonly HashSet<ScrollViewer> Hooked = new();

    static AutoScroll()
    {
        ToEndTokenProperty.Changed.AddClassHandler<ScrollViewer>(OnTokenChanged);
    }

    public static int GetToEndToken(ScrollViewer viewer) => viewer.GetValue(ToEndTokenProperty);

    public static void SetToEndToken(ScrollViewer viewer, int value) =>
        viewer.SetValue(ToEndTokenProperty, value);

    private static void OnTokenChanged(ScrollViewer viewer, AvaloniaPropertyChangedEventArgs e)
    {
        Hook(viewer);
        ScrollToEnd(viewer);
    }

    private static void Hook(ScrollViewer viewer)
    {
        if (!Hooked.Add(viewer))
            return;

        // Auch beim ersten Layout (Extent wächst von 0 auf die Diagrammbreite)
        // und bei einer Größenänderung ans Ende springen.
        viewer.PropertyChanged += (_, args) =>
        {
            if (args.Property == ScrollViewer.ExtentProperty)
                ScrollToEnd(viewer);
        };
    }

    private static void ScrollToEnd(ScrollViewer viewer)
    {
        // Nach dem Layout ausführen, damit Extent/Viewport bereits aktuell sind.
        Dispatcher.UIThread.Post(() =>
        {
            double max = Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width);
            if (max > 0)
                viewer.Offset = new Vector(max, viewer.Offset.Y);
        }, DispatcherPriority.Background);
    }
}

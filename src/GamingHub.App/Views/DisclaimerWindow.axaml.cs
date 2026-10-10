using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace GamingHub.App.Views;

public partial class DisclaimerWindow : Window
{
    /// <summary>Der sichtbare Textinhalt (Kurzfassung) für die Lesezeit-Schätzung.</summary>
    private const string DisclosureText =
        "Gaming Hub ist eine kostenlose Software „wie besehen“ (as-is). Nutzung auf eigene Gefahr: " +
        "Wir haften nicht für Schäden an Hard- oder Software, für Datenverlust, Leistungsverlust, " +
        "Systeminstabilität oder sonstige direkte oder indirekte Schäden, die durch Installation, " +
        "Nutzung, Start/Stopp-Steuerung von Spielen, das Bereinigen von Dateien oder andere Funktionen " +
        "entstehen. Das Cleanup-Modul kann Dateien dauerhaft oder in den Papierkorb löschen – prüfe jede " +
        "Auswahl sorgfältig und lege Backups an. Kompatibilitäts-Einschätzungen sind unverbindliche " +
        "Schätzungen. Es besteht kein Anspruch auf Support, Fehlerbehebung oder Updates. Mit „Akzeptieren“ " +
        "bestätigst du, dies gelesen und verstanden zu haben und die App auf eigene Gefahr zu nutzen.";

    public bool Accepted { get; private set; }

    private readonly int _minReadSeconds;
    private int _remaining;
    private DispatcherTimer? _timer;

    public DisclaimerWindow()
    {
        InitializeComponent();

        _minReadSeconds = ComputeReadingSeconds(DisclosureText);
        _remaining = _minReadSeconds;
        UpdateGate();

        AcceptCheck.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                UpdateAcceptEnabled();
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            UpdateGate();
            if (_remaining <= 0) _timer?.Stop();
        };
        _timer.Start();
    }

    private void UpdateGate()
    {
        if (_remaining > 0)
        {
            ReadHint.Text = $"Bitte lies den Haftungsausschluss vollständig – noch {_remaining} s.";
        }
        else
        {
            ReadHint.Text = string.Empty;
            _timer?.Stop();
        }
        UpdateAcceptEnabled();
    }

    private void UpdateAcceptEnabled()
    {
        AcceptButton.IsEnabled = _remaining <= 0 && AcceptCheck.IsChecked == true;
    }

    /// <summary>Lesezeit-Gate: ca. 4,5 Wörter pro Sekunde, mindestens 8 und höchstens 30 Sekunden.</summary>
    private static int ComputeReadingSeconds(string text)
    {
        int words = text.Split(
            new[] { ' ', '\n', '\r' },
            System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries).Length;
        return Math.Clamp((int)Math.Ceiling(words / 4.5), 8, 30);
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        if (AcceptButton.IsEnabled && AcceptCheck.IsChecked == true)
        {
            Accepted = true;
            Close();
        }
    }

    private void OnDeclineClick(object? sender, RoutedEventArgs e)
    {
        Accepted = false;
        Close();
    }
}
namespace GamingHub.App;

/// <summary>Eine versionierte, benutzerfreundliche Änderungsliste.</summary>
public sealed record ChangelogEntry(string Version, string Date, string[] Items);

/// <summary>Im Programm angezeigte Neuerungen (aus Nutzersicht).</summary>
public static class Changelog
{
    public static readonly ChangelogEntry[] Entries =
    {
        new("0.4.0", "Oktober 2026", new[]
        {
            "Kommunikation: Direktnachrichten per E-Mail-Adresse, Antworten auf Nachrichten und Dateiversand.",
            "Freundesliste mit Anfragen annehmen/ablehnen sowie Blockieren von Nutzern.",
            "Administratoren können einzelne Funktionen für alle freischalten oder deaktivieren.",
            "System-Ressourcen: Netzwerk- und Festplatten-Auslastung funktionieren jetzt zuverlässig.",
            "Disk Cleanup: Fortschritt in Prozent und Anzeige der benötigten Zeit.",
            "Spielebibliothek nach Kategorien als Reiter filterbar."
        }),
        new("0.3.1", "Oktober 2026", new[]
        {
            "Automatisches Update beim Start wurde zuverlässig gemacht."
        }),
        new("0.3.0", "Oktober 2026", new[]
        {
            "Textchat mit mehreren Kanälen und Direktnachrichten.",
            "Sprachanrufe und Videoanrufe zwischen Nutzern.",
            "Benachrichtigungen mit Ton und Desktop-Hinweisen."
        })
    };
}

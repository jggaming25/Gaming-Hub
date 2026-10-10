namespace GamingHub.App;

/// <summary>Eine versionierte, benutzerfreundliche Änderungsliste.</summary>
public sealed record ChangelogEntry(string Version, string Date, string[] Items);

/// <summary>Im Programm angezeigte Neuerungen (aus Nutzersicht).</summary>
public static class Changelog
{
    public static readonly ChangelogEntry[] Entries =
    {
        new("0.4.1", "Oktober 2026", new[]
        {
            "Geschriebene Nachrichten erscheinen jetzt sofort im Chat (zuvor wurden sie teilweise erst nach Stunden sichtbar).",
            "Anruf & Videoanruf nur noch in Direktnachrichten verfügbar (nicht im Allgemein-Chat).",
            "Präzisere Empfehlung im System-Check: bekannte Spiele werden gegen ihre tatsächlichen Mindest-/Empfehlungswerte geprüft.",
            "Web-Recherche zu Systemanforderungen verbessert (findet jetzt auch OMSI 2 & Co. korrekt).",
            "Programm-Log für die Fehlersuche – wird bei Bugreports automatisch angehängt (Einstellungen → Programm-Log).",
            "Automatische Updates werden zuverlässiger installiert und melden Fehler besser.",
            "F11 für Vollbild · Prozesse im System-Check lassen sich jetzt beenden.",
            "Dunkles Design überarbeitet (helles Design optional) · Einheiten bei „Frei“ auf Laufwerken ergänzt.",
            "Programm-Kategorien verbessert (openCode & Co. landen korrekt unter KI-Tools)."
        }),
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

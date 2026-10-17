namespace GamingHub.App;

/// <summary>Eine versionierte, benutzerfreundliche Änderungsliste.</summary>
public sealed record ChangelogEntry(string Version, string Date, string[] Items);

/// <summary>Im Programm angezeigte Neuerungen (aus Nutzersicht).</summary>
public static class Changelog
{
    public static readonly ChangelogEntry[] Entries =
    {
        new("0.5.15", "Oktober 2026", new[]
        {
            "OpenRGB wird jetzt automatisch mit der App installiert – kein manueller Download mehr nötig.",
            "Gaming Hub prüft beim Start, ob OpenRGB vorhanden und aktuell ist, lädt es bei Bedarf von openrgb.org und hält es mit Updates aktuell.",
            "Der Beleuchtungs-Tab zeigt den Installations- und Update-Fortschritt."
        }),
        new("0.5.14", "Oktober 2026", new[]
        {
            "Beleuchtung (RGB) komplett neu: Gaming Hub spricht jetzt direkt mit dem OpenRGB-SDK. Maus, Tastatur, Lüfter, GPU und Mainboard werden zuverlässig erkannt und lassen sich steuern.",
            "Farbe und Effekt pro Gerät: es werden genau die Modi angezeigt, die das jeweilige Gerät wirklich unterstützt.",
            "Läuft OpenRGB nicht, kann es direkt aus der App gestartet werden – mit Hinweis zur Einrichtung des SDK-Servers (Port 6742).",
            "Website: Downloads früherer Versionen sind wieder verfügbar."
        }),
        new("0.5.13", "Oktober 2026", new[]
        {
            "Spiele lassen sich wieder starten und stoppen: Jede Karte zeigt direkt „Starten“/„Abbrechen“, ein Klick auf das Bild öffnet die Detailansicht.",
            "Der Beleuchtungs-Tab (RGB) ist wieder im Menü vorhanden.",
            "Laufwerksfilter repariert: Wählt man ein einzelnes Laufwerk, werden die Spiele wieder angezeigt.",
            "Spiele-Logos jetzt in deutlich höherer Auflösung (bis 256×256 statt 32×32)."
        }),
        new("0.5.12", "Oktober 2026", new[]
        {
            "Passwortschutz pro App: Im Spiele-Tab lässt sich je Eintrag ein Passwortschutz per Regler aktivieren; zum Starten wird das Konto-Passwort verlangt.",
            "Spiele-Tab neu: Logo/Vorschaubild, Klick zum Aufklappen mit Details, Start- (grün) und „Abbrechen“-Knopf (blau).",
            "Premium-Status im Profil: Countdown mit verbleibender Zeit und Fortschrittsbalken.",
            "Premium-Codes: Laufzeit/Gültig-bis beim Erstellen, stapelt auf bestehendes Premium, pro Nutzer einmal einlösbar – die Anzeige „um 0 Tage“ wurde korrigiert.",
            "Internet-Checker (Premium/Admin): visueller Pfad-Test, Ausfall-Einschätzung (Router vs. ISP) und scrollbarer Verlauf – 1 Woche frei, mit Premium 4 Wochen inkl. Datums-Anzeige.",
            "Neuer „Beleuchtung“-Tab: RGB-Steuerung für Maus, Tastatur, Lüfter, GPU und Mainboard (OpenRGB).",
            "Spiel-Icons in der Bibliothek (automatische Extraktion).",
            "GPU-Auslastung korrekt erfasst, Disk-Cleanup-Restzeit immer sichtbar (10-Sekunden-Takt).",
            "Downloads: neueste Version für alle, ältere 5 per Admin-Schalter sperrbar.",
            "Chat/Voice/Video wurden entfernt – schlanker und stabiler.",
            "Diese Neuerungen („Neuigkeiten“) werden ab jetzt mit jedem Update aktuell gehalten."
        }),
        new("0.5.6", "Oktober 2026", new[]
        {
            "Internet-Checker als neue Funktion (Premium/Admin).",
            "Beleuchtung (RGB) für Maus, Tastatur, Lüfter, GPU und Mainboard.",
            "Spiel-Icons in der Spielebibliothek.",
            "Premium-Codes mit Laufzeit und Gültig-bis; Downloads per Admin-Flag sperrbar."
        }),
        new("0.5.0", "Oktober 2026", new[]
        {
            "Premium ohne Kauf: Freischalt-Codes sind pro Nutzer nur einmal einlösbar; Admins können Premium sperren.",
            "Neues Abzeichen „Premium-Veteran“ nach 3 Monaten Premium.",
            "Frühere Versionen sind auf der Website dauerhaft herunterladbar.",
            "Web-Recherche berücksichtigt die Spielversion korrekt (z. B. OMSI 2 statt OMSI 1)."
        }),
        new("0.4.2", "Oktober 2026", new[]
        {
            "Beim Start werden verfügbare Updates automatisch installiert – danach zeigt ein Popup, dass ein Neustart zum Aktivieren nötig ist.",
            "Gaming Hub startet automatisch mit Windows und läuft dabei leise im Hintergrund (Infobereich).",
            "Moderne Navigationsleiste mit abgerundetem Logo und neuem Design.",
            "Spielebibliothek: Beim Start werden nur noch Änderungen übernommen (kein kompletter Neuscann mehr) – zusätzlich „Vollständiger Scan“-Knopf.",
            "System-Ressourcen: Fehlgeschlagene Werte werden automatisch 2× nach je 20 Sekunden erneut abgefragt; GPU-Auslastung wird jetzt korrekt erfasst.",
            "Neuer „Verlauf“-Bereich: alle 5 Minuten aufgezeichnete CPU-/RAM-/GPU-/Prozess-Daten als Diagramm (1 Woche, mit Premium 4 Wochen).",
            "Premium ohne Kauf: Freischalt-Codes einlösen oder durch Abzeichen (Bibliothekar, Systemprofi, Aufräumer, Bugreporter) verdienen.",
            "Haftungsausschluss muss nach jedem Update mit Lesezeit-Gate erneut bestätigt werden.",
            "Disk Cleanup: Hinweis zur Dauer (ca. 2 TB ≈ 15–20 Min.) und Live-Restzeitanzeige.",
            "Web-Recherche berücksichtigt optional eine Spielversion (z. B. „2.1“) für präzisere Systemanforderungen.",
            "Chat: Eigene Nachrichten erscheinen sofort (kein stundenlanges Warten mehr)."
        }),
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

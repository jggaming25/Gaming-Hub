namespace GamingHub.App;

/// <summary>Eine versionierte, benutzerfreundliche Änderungsliste.</summary>
public sealed record ChangelogEntry(string Version, string Date, string[] Items);

/// <summary>Im Programm angezeigte Neuerungen (aus Nutzersicht).</summary>
public static class Changelog
{
    public static readonly ChangelogEntry[] Entries =
    {
        new("0.5.24", "19.10.2026 17:15", new[]
        {
            "Bugfix Beleuchtung: Farben und Effekte werden jetzt zuverlässig auf die Geräte angewendet. Ursache war eine vorhandene fremde OpenRGB-Installation auf dem Standardport, die die Geräte nicht steuern konnte – Gaming Hub startet und nutzt nun eine eigene, unabhängige OpenRGB-Instanz.",
            "Beleuchtung: Die Ansicht zeigt jetzt eine Rückmeldung an (z. B. „Farbe gesetzt“ oder „Fehler beim Setzen“), die nicht mehr automatisch von der Verbindungsanzeige überschrieben wird."
        }),
        new("0.5.23", "19.10.2026 16:33", new[]
        {
            "Bugfix Beleuchtung: Beim Anwenden eines Effekts werden die gewählten Farben jetzt wirklich übernommen. Einfarbige Modi erhalten genau die (gemischte) Farbe, mehrfarbige Modi einen Verlauf, und Modi ohne Farbauswahl (z. B. fester Regenbogen) werden nicht mehr abgelehnt.",
            "Profile: Beim Laden wird auch bei einfarbigen Effekten die gemischte Farbe verwendet."
        }),
        new("0.5.22", "19.10.2026 16:15", new[]
        {
            "Konto: Registrierung mit E-Mail-Bestätigung zeigt jetzt einen klaren Hinweis und meldet sich nicht mehr fälschlich als angemeldet an; ein fehlendes Profil wird automatisch angelegt.",
            "„Kann ich das spielen?“: Spiele lassen sich jetzt frei eintippen (mit Vorschlägen). Für Steam-Spiele werden die echten Mindest- und Empfehlungsanforderungen angezeigt, und fremde Treffer (z. B. „ProinHub“) werden ausgefiltert.",
            "Beleuchtung: Zwei Farben lassen sich mischen (Anteil per Regler) und als Farbverlauf auf Effekte anwenden; Profile mit Farbverlauf werden gespeichert und aktualisiert.",
            "Benachrichtigungen: Echte Windows-Benachrichtigungen mit Test-Knopf und Direktlink zu den Windows-Einstellungen; die Anzeige in der App bleibt als Fallback bestehen.",
            "Spielebibliothek: Netzlaufwerke erscheinen jetzt in der Laufwerksliste (auch nicht verbundene) – inklusive der Rechte des angemeldeten Benutzers (Lesen/Schreiben)."
        }),
        new("0.5.21", "19.10.2026 14:50", new[]
        {
            "Beleuchtung: Geräte lassen sich jetzt direkt auswählen; technische OpenRGB-Statusanzeigen sowie „Trennen“ und „Neu verbinden“ wurden entfernt.",
            "Einstellungen: neuer Untertab „Kontakt“ mit Ansprechpartner, Discord und E-Mail; doppelte Admin-Optionen wurden behoben.",
            "Lizenz: Der vollständige Lizenztext wird direkt in der App unter Einstellungen → Lizenz angezeigt – ohne GitHub-Verweis.",
            "System-Ressourcen: GPU-Auslastung wird zuverlässig über NVIDIA NVML gelesen (kein „n. v.“ mehr); die Verlaufsdiagramme haben jetzt eine Datums-/Uhrzeitachse mit Rasterlinien.",
            "Spielebibliothek: Die Statuszeile wird nicht mehr abgeschnitten; FiveM zählt als Spiel und „Sonstige“-Einträge werden im Hintergrund per Web-Recherche eingeordnet.",
            "„Kann ich das spielen?“: Die Recherche liefert keine falsche Version mehr (z. B. OMSI 1 statt OMSI 2); alte Ergebnisse verschwinden bei einer neuen Berechnung.",
            "Datenschutz: Supabase wird im Haftungsausschluss nicht mehr genannt; Bugreports gehen im Fallback per E-Mail statt über GitHub."
        }),
        new("0.5.20", "19.10.2026 14:12", new[]
        {
            "Spielebibliothek neu: übersichtliche Karten mit Logo, Quelle, Dateipfad und Größe; Kategorien für Kommunikation, Entwicklung und Browser; fehlende Ordnergrößen werden automatisch nachgetragen.",
            "Neue Admin-Statistik: Downloads aller Versionen, registrierte Nutzer und Alter der App – nur für Admins sichtbar.",
            "System-Ressourcen: jetzt alle Prozesse mit Sortierung nach CPU, Arbeitsspeicher oder Name und Anheft-Funktion; die Liste springt nicht mehr bei jeder Messung.",
            "Internet-Checker: Testseiten mit Fallback (Google → YouTube → Cloudflare), Download-Messung mit 7-Tage-Durchschnitt und Hinweis bei auffällig langsamer Verbindung.",
            "Beleuchtung: Farb-Presets, zuverlässige Farb-/Zonen-Buttons (automatischer Direct-Modus) und direkte RGB-Eingabe.",
            "Disk Cleanup: Scan-Bericht als Textdatei exportierbar, klarer Hinweis zu endgültigem Löschen und stabilere Duplikat-Ansicht.",
            "Sicherheit: gespeicherte Anmeldung jetzt mit Windows-DPAPI verschlüsselt, strengere Datenbankregeln und geschützte Admin-Funktionen.",
            "Nur noch eine App-Instanz gleichzeitig – ein zweiter Start holt das laufende Fenster nach vorne.",
            "Einstellungen mit Lizenz- und Datenschutz-Hinweisen; Änderungsliste jetzt mit Datum und Uhrzeit."
        }),
        new("0.5.19", "17.10.2026 15:25", new[]
        {
            "Bugfix: Der Start hängt nicht mehr bei „Initialisiere…“ – das Startfenster blieb gelegentlich stehen und das Hauptfenster öffnete sich nicht.",
            "Ursache war der Splash-Ablauf: Er wurde erst nach dem Anzeigen des Fensters registriert und konnte dadurch übersprungen werden. Er wird jetzt zuverlässig gestartet.",
            "Zusätzlich hat die Update-Prüfung beim Start ein Zeitlimit, damit der Start nie hängen bleibt."
        }),
        new("0.5.18", "17.10.2026 14:50", new[]
        {
            "Bugfix: Das Installieren von Updates bricht nicht mehr ab und die App startet nicht mehr endlos mit „Neustart erforderlich“ neu.",
            "Ursache war OpenRGB: Es lief im App-Installationsordner und blockierte so das Update. OpenRGB startet jetzt in seinem eigenen Ordner und wird vor dem Update automatisch beendet.",
            "Beleuchtung: OpenRGB wird weiterhin automatisch mit der App gestartet und beim Update sauber neu geladen."
        }),
        new("0.5.17", "17.10.2026 14:23", new[]
        {
            "Bugfix: Nach dem Akzeptieren der Nutzungsbedingungen startet die App jetzt zuverlässig weiter – Splash- und Hauptfenster werden wieder angezeigt.",
            "Beleuchtung: Beim Auswählen eines Geräts mit schwarzer Standardfarbe wird automatisch eine sichtbare Farbe gesetzt, damit „Farbe anwenden“ die LEDs nicht unbeabsichtigt ausschaltet."
        }),
        new("0.5.16", "17.10.2026 14:10", new[]
        {
            "Beleuchtung: OpenRGB wird jetzt mit aktiviertem SDK-Server gestartet – Geräte (z. B. Corsair-Tastatur) werden erkannt und sind steuerbar.",
            "Läuft OpenRGB bereits ohne SDK-Server, wird es automatisch mit Server neu gestartet.",
            "Deutlich flüssigere App: System-/Prozess-Messungen und die Spielerkennung laufen jetzt im Hintergrund statt im UI-Thread (behebt das Ruckeln)."
        }),
        new("0.5.15", "17.10.2026 13:13", new[]
        {
            "OpenRGB wird jetzt automatisch mit der App installiert – kein manueller Download mehr nötig.",
            "Gaming Hub prüft beim Start, ob OpenRGB vorhanden und aktuell ist, lädt es bei Bedarf von openrgb.org und hält es mit Updates aktuell.",
            "Der Beleuchtungs-Tab zeigt den Installations- und Update-Fortschritt."
        }),
        new("0.5.14", "17.10.2026 12:47", new[]
        {
            "Beleuchtung (RGB) komplett neu: Gaming Hub spricht jetzt direkt mit dem OpenRGB-SDK. Maus, Tastatur, Lüfter, GPU und Mainboard werden zuverlässig erkannt und lassen sich steuern.",
            "Farbe und Effekt pro Gerät: es werden genau die Modi angezeigt, die das jeweilige Gerät wirklich unterstützt.",
            "Läuft OpenRGB nicht, kann es direkt aus der App gestartet werden – mit Hinweis zur Einrichtung des SDK-Servers (Port 6742).",
            "Website: Downloads früherer Versionen sind wieder verfügbar."
        }),
        new("0.5.13", "17.10.2026 11:18", new[]
        {
            "Spiele lassen sich wieder starten und stoppen: Jede Karte zeigt direkt „Starten“/„Abbrechen“, ein Klick auf das Bild öffnet die Detailansicht.",
            "Der Beleuchtungs-Tab (RGB) ist wieder im Menü vorhanden.",
            "Laufwerksfilter repariert: Wählt man ein einzelnes Laufwerk, werden die Spiele wieder angezeigt.",
            "Spiele-Logos jetzt in deutlich höherer Auflösung (bis 256×256 statt 32×32)."
        }),
        new("0.5.12", "17.10.2026 10:54", new[]
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
        new("0.5.6", "11.10.2026 01:27", new[]
        {
            "Internet-Checker als neue Funktion (Premium/Admin).",
            "Beleuchtung (RGB) für Maus, Tastatur, Lüfter, GPU und Mainboard.",
            "Spiel-Icons in der Spielebibliothek.",
            "Premium-Codes mit Laufzeit und Gültig-bis; Downloads per Admin-Flag sperrbar."
        }),
        new("0.5.0", "10.10.2026 22:12", new[]
        {
            "Premium ohne Kauf: Freischalt-Codes sind pro Nutzer nur einmal einlösbar; Admins können Premium sperren.",
            "Neues Abzeichen „Premium-Veteran“ nach 3 Monaten Premium.",
            "Frühere Versionen sind auf der Website dauerhaft herunterladbar.",
            "Web-Recherche berücksichtigt die Spielversion korrekt (z. B. OMSI 2 statt OMSI 1)."
        }),
        new("0.4.2", "10.10.2026 20:50", new[]
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
        new("0.4.1", "10.10.2026 19:20", new[]
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
        new("0.4.0", "10.10.2026 18:07", new[]
        {
            "Kommunikation: Direktnachrichten per E-Mail-Adresse, Antworten auf Nachrichten und Dateiversand.",
            "Freundesliste mit Anfragen annehmen/ablehnen sowie Blockieren von Nutzern.",
            "Administratoren können einzelne Funktionen für alle freischalten oder deaktivieren.",
            "System-Ressourcen: Netzwerk- und Festplatten-Auslastung funktionieren jetzt zuverlässig.",
            "Disk Cleanup: Fortschritt in Prozent und Anzeige der benötigten Zeit.",
            "Spielebibliothek nach Kategorien als Reiter filterbar."
        }),
        new("0.3.1", "10.10.2026 17:08", new[]
        {
            "Automatisches Update beim Start wurde zuverlässig gemacht."
        }),
        new("0.3.0", "10.10.2026 17:03", new[]
        {
            "Textchat mit mehreren Kanälen und Direktnachrichten.",
            "Sprachanrufe und Videoanrufe zwischen Nutzern.",
            "Benachrichtigungen mit Ton und Desktop-Hinweisen."
        })
    };
}

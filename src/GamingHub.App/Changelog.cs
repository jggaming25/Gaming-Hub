namespace GamingHub.App;

/// <summary>Eine versionierte, benutzerfreundliche Änderungsliste.</summary>
public sealed record ChangelogEntry(string Version, string Date, string[] Items);

/// <summary>Im Programm angezeigte Neuerungen (aus Nutzersicht).</summary>
public static class Changelog
{
    public static readonly ChangelogEntry[] Entries =
    {
        new("0.5.33", "22.10.2026 15:00", new[]
        {
            "Neu: Bildschirmzeit – Nutzung pro App verfolgen, Tages- und Wochenlimits festlegen, erlaubte Zeitfenster definieren und eigene Profile speichern. Bei erreichtem Limit erscheint ein Sperrbildschirm mit „15 Minuten hinzufügen“ und „Für heute ausschalten“.",
            "Premium: Bildschirmzeit ist exklusiv für Premium-Nutzer; Admins behalten vollen Zugriff. Das Internet-Checker-Diagramm, die Bewertung und der Verlauf sind jetzt ebenfalls Premium-Funktionen.",
            "Beleuchtung: Geräte werden beim Aktualisieren nicht mehr zurückgesetzt; die Auswahl bleibt stabil. „Farbwechsel“ ist jetzt direkt als Effekt wählbar, und Farben werden erst mit „Farbe anwenden“ bzw. „Effekt anwenden“ übernommen.",
            "Beleuchtung: Die Zweitfarbe bleibt beim Wechsel des Effekts erhalten und wird zusammen mit Farbwechsel-Profilen gespeichert.",
            "Anmeldung: Der Discord-Login nutzt eine zuverlässige Weiterleitung über die Website sowie IPv6- und IPv4-Unterstützung am lokalen Rückkehr-Port.",
            "E-Mail: Support- und Bugreport-Links öffnen jetzt zuverlässig einen vorbereiteten Gmail-Entwurf statt eines möglicherweise fehlenden Standard-Mailprogramms.",
            "Navigation: Die Tabs sind jetzt logisch sortiert – von Spiele und System über Wartung und Premium-Funktionen bis zu Hilfe, Einstellungen und Statistik."
        }),
        new("0.5.32", "22.10.2026 02:50", new[]
        {
            "Update-Suche: Die App nutzt jetzt den offiziellen Release-Feed statt der GitHub-API. Dadurch funktioniert die Suche auch dann zuverlässig, wenn das GitHub-API-Limit erreicht ist (Fehler 403).",
            "Update-Installation: Neue Versionen werden weiterhin direkt aus dem neuesten Release geladen und wie gewohnt installiert."
        }),
        new("0.5.31", "22.10.2026 02:35", new[]
        {
            "Premium: Treiber und Beleuchtung bleiben jetzt auch ohne Premium in der Navigation sichtbar, sind aber mit einem Schloss markiert.",
            "Premium: Wird ein gesperrter Bereich geöffnet, erscheint ein ausgegrauter Bereich mit dem Hinweis „Nur mit Premium nutzbar“ und einem direkten Link zum Profil.",
            "Premium: Die eigentlichen Funktionen bleiben ohne aktives Premium weiterhin deaktiviert; Admins behalten vollen Zugriff."
        }),
        new("0.5.30", "22.10.2026 02:20", new[]
        {
            "Premium: Der Treiber-Tab und der Beleuchtungs-Tab sind jetzt nur noch mit aktivem Premium nutzbar. Admins behalten weiterhin Zugriff.",
            "Premium: Läuft Premium ab, werden beide Tabs automatisch ausgeblendet; im Profil sind die freigeschalteten Vorteile beschrieben.",
            "Beleuchtung: Ohne Premium wird OpenRGB nicht mehr automatisch im Hintergrund gestartet."
        }),
        new("0.5.29", "22.10.2026 02:07", new[]
        {
            "Benachrichtigungen: Windows-Toasts informieren jetzt, wenn eine Disk-Cleanup-Prüfung, das Löschen oder der Full Auto Clean abgeschlossen ist.",
            "Benachrichtigungen: Auch Treiberprüfung, Treiberinstallation und ein vollständiger Spielbibliothek-Scan melden ihren Abschluss als Windows-Toast.",
            "Einstellungen: Die Beschreibung der Desktop-Benachrichtigungen nennt jetzt auch abgeschlossene Aufgaben."
        }),
        new("0.5.28", "22.10.2026 01:39", new[]
        {
            "System-Ressourcen: Die große Laufzeit zählt jetzt seit der letzten Aktivierung – also seit dem letzten Start oder Aufwachen aus dem Standby.",
            "Zusätzlich werden die Windows-Betriebszeit seit dem letzten vollständigen Start (inklusive Standby) und der letzte vollständige Start separat angezeigt.",
            "Damit ist klar, warum Windows nach dem Standby eine höhere Laufzeit anzeigt als die gefühlte Zeit seit dem Einschalten; das Windows-Alter wird getrennt ausgewiesen.",
            "Die kopierten Systeminformationen und der Hilfe-Tab erklären die beiden Laufzeit-Werte jetzt ebenfalls."
        }),
        new("0.5.27", "22.10.2026 01:28", new[]
        {
            "Spielebibliothek: EXE und Installationsordner werden jetzt nur noch angezeigt, wenn sie wirklich erkannt wurden – fehlende Angaben erscheinen nicht mehr als Platzhalter.",
            "Spielebibliothek: Die automatische Erkennung sucht jetzt auch in tieferen Unterordnern nach der passenden Startdatei.",
            "Neu: Umfangreicher Hilfe-Tab mit Lösungen für fehlende Spielangaben, Windows-Benachrichtigungen, Beleuchtung, Treiber, Internet und Update-Probleme.",
            "Hilfe: Direkte Schaltflächen zu Windows-Benachrichtigungen, Windows Update, Netzwerkstatus, Geräte-Manager, Log- und Datenordner sowie eine kopierbare Diagnose für den Support."
        }),
        new("0.5.26", "22.10.2026 01:08", new[]
        {
            "Beleuchtung: Neuer automatischer Farbwechsel – die gewählte Farbe wechselt im eingestellten Intervall (2–120 Sekunden) mit der Zweitfarbe. Funktioniert auch bei Geräten, die nur den Direct-Modus unterstützen.",
            "Beleuchtung: Die Farben bleiben jetzt dauerhaft aktiv, statt nach einigen Sekunden auszufallen.",
            "Beleuchtung: „Profil laden“ und das Löschen von Profilen funktionieren wieder zuverlässig.",
            "Neu: Treiber-Ansicht mit allen installierten Treibern (Hersteller, Version, Datum, Gerät), Suche über Windows Update und Installation gefundener Updates – bei Bedarf mit Administratorabfrage.",
            "System-Ressourcen: Installationsdatum von Windows, Systemlaufzeit und Startzeit werden angezeigt; zusätzlich eine Übersicht mit CPU, GPU, RAM, Mainboard und Windows-Version, die sich per Knopfdruck kopieren lässt.",
            "Internet-Checker: Der komplette Verbindungsweg wird jetzt als übersichtliches Diagramm geprüft – vom Netzwerkadapter über Router und DNS bis zum Internet, HTTPS und einem echten Download-Test.",
            "Spielebibliothek: Zu jedem Eintrag werden, wenn vorhanden, die ausführbare Datei, der Installationsordner und die tatsächliche Ordnergröße direkt angezeigt.",
            "Benachrichtigungen: Windows-Toasts lassen sich jetzt zuverlässig testen. Wird die Anzeige durch Windows blockiert (Fokusassistent, Vollbild, Stummschaltung), zeigt die App den Grund und öffnet auf Wunsch direkt die passende Windows-Einstellung.",
            "Einstellungen: neuer Untertab „Impressum“ mit freiwilligen Kontaktangaben und Hinweis zum privaten Hobbyprojekt."
        }),
        new("0.5.25", "19.10.2026 18:55", new[]
        {
            "Bugfix Anmeldung: Die Supabase-Sitzung wird jetzt automatisch erneuert (beim Start, regelmäßig im Hintergrund und vor der Admin-Statistik). Nach Ablauf des Tokens zeigt die App nicht mehr fälschlich „nicht angemeldet“ an.",
            "Admin-Statistik: Registrierte Nutzer werden wieder zuverlässig angezeigt.",
            "Die erneuerte Sitzung wird verschlüsselt gespeichert, damit die Anmeldung auch nach einem Neustart erhalten bleibt."
        }),
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

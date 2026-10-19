import { useEffect, useState } from "react";
import type { Session } from "@supabase/supabase-js";
import { supabase, isCurrentUserAdmin, isDownloadsEnabled } from "./supabase";
import { BugTracker } from "./BugTracker";
import { Admin } from "./Admin";
import {
  DOWNLOAD_SETUP_URL,
  DOWNLOAD_PORTABLE_URL,
  fetchReleases,

  type ReleaseInfo,
  isSupabaseConfigured
} from "./config";

const FEATURES = [
  { icon: "🎮", title: "Spielbibliothek", text: "Steam, Epic und installierte Programme automatisch erkennen, starten, stoppen und Spielzeiten verfolgen." },
  { icon: "🧹", title: "Disk Cleanup", text: "Doppelte Dateien finden und mit einem Klick aufräumen – sicher über den Papierkorb." },
  { icon: "📊", title: "System-Check", text: "Live CPU-, RAM-, Netzwerk- und Festplatten-Auslastung plus Einschätzung, welche Spiele auf deinem PC laufen." },
  { icon: "🏆", title: "Abzeichen", text: "Sammle Erfolge, z. B. das Early-Bird-Abzeichen für frühe Nutzer 2026." },
  { icon: "🐞", title: "Bugtracker", text: "Probleme direkt melden und den Bearbeitungsstatus im Blick behalten." }
];

const CHANGELOG: { version: string; date: string; items: string[] }[] = [
  {
    version: "0.5.24",
    date: "19.10.2026 17:15",
    items: [
      "Bugfix Beleuchtung: Farben und Effekte werden jetzt zuverlässig auf die Geräte angewendet. Ursache war eine vorhandene fremde OpenRGB-Installation auf dem Standardport, die die Geräte nicht steuern konnte – Gaming Hub startet und nutzt nun eine eigene, unabhängige OpenRGB-Instanz.",
      "Beleuchtung: Die Ansicht zeigt jetzt eine Rückmeldung an (z. B. „Farbe gesetzt“ oder „Fehler beim Setzen“), die nicht mehr automatisch von der Verbindungsanzeige überschrieben wird."
    ]
  },
  {
    version: "0.5.23",
    date: "19.10.2026 16:33",
    items: [
      "Bugfix Beleuchtung: Beim Anwenden eines Effekts werden die gewählten Farben jetzt wirklich übernommen. Einfarbige Modi erhalten genau die (gemischte) Farbe, mehrfarbige Modi einen Verlauf, und Modi ohne Farbauswahl (z. B. fester Regenbogen) werden nicht mehr abgelehnt.",
      "Profile: Beim Laden wird auch bei einfarbigen Effekten die gemischte Farbe verwendet."
    ]
  },
  {
    version: "0.5.22",
    date: "19.10.2026 16:15",
    items: [
      "Konto: Registrierung mit E-Mail-Bestätigung zeigt jetzt einen klaren Hinweis und meldet sich nicht mehr fälschlich als angemeldet an; ein fehlendes Profil wird automatisch angelegt.",
      "„Kann ich das spielen?“: Spiele lassen sich jetzt frei eintippen (mit Vorschlägen). Für Steam-Spiele werden die echten Mindest- und Empfehlungsanforderungen angezeigt, und fremde Treffer werden ausgefiltert.",
      "Beleuchtung: Zwei Farben lassen sich mischen (Anteil per Regler) und als Farbverlauf auf Effekte anwenden; Profile mit Farbverlauf werden gespeichert und aktualisiert.",
      "Benachrichtigungen: Echte Windows-Benachrichtigungen mit Test-Knopf und Direktlink zu den Windows-Einstellungen; die Anzeige in der App bleibt als Fallback bestehen.",
      "Spielebibliothek: Netzlaufwerke erscheinen jetzt in der Laufwerksliste (auch nicht verbundene) – inklusive der Rechte des angemeldeten Benutzers (Lesen/Schreiben)."
    ]
  },
  {
    version: "0.5.21",
    date: "19.10.2026 14:50",
    items: [
      "Beleuchtung: Geräte lassen sich jetzt direkt auswählen; technische OpenRGB-Statusanzeigen sowie „Trennen“ und „Neu verbinden“ wurden entfernt.",
      "Einstellungen: neuer Untertab „Kontakt“ mit Ansprechpartner, Discord und E-Mail; doppelte Admin-Optionen wurden behoben.",
      "Lizenz: Der vollständige Lizenztext wird direkt in der App unter Einstellungen → Lizenz angezeigt – ohne GitHub-Verweis.",
      "System-Ressourcen: GPU-Auslastung wird zuverlässig über NVIDIA NVML gelesen (kein „n. v.“ mehr); die Verlaufsdiagramme haben jetzt eine Datums-/Uhrzeitachse mit Rasterlinien.",
      "Spielebibliothek: Die Statuszeile wird nicht mehr abgeschnitten; FiveM zählt als Spiel und „Sonstige“-Einträge werden im Hintergrund per Web-Recherche eingeordnet.",
      "„Kann ich das spielen?“: Die Recherche liefert keine falsche Version mehr (z. B. OMSI 1 statt OMSI 2); alte Ergebnisse verschwinden bei einer neuen Berechnung.",
      "Datenschutz: Supabase wird im Haftungsausschluss nicht mehr genannt; Bugreports gehen im Fallback per E-Mail statt über GitHub."
    ]
  },
  {
    version: "0.5.20",
    date: "19.10.2026 14:12",
    items: [
      "Spielebibliothek neu: übersichtliche Karten mit Logo, Quelle, Dateipfad und Größe; Kategorien für Kommunikation, Entwicklung und Browser; fehlende Ordnergrößen werden automatisch nachgetragen.",
      "Neue Admin-Statistik: Downloads aller Versionen, registrierte Nutzer und Alter der App – nur für Admins sichtbar.",
      "System-Ressourcen: alle Prozesse mit Sortierung nach CPU, Arbeitsspeicher oder Name und Anheft-Funktion; die Liste springt nicht mehr bei jeder Messung.",
      "Internet-Checker: Testseiten mit Fallback (Google → YouTube → Cloudflare), Download-Messung mit 7-Tage-Durchschnitt und Hinweis bei auffällig langsamer Verbindung.",
      "Beleuchtung: Farb-Presets, zuverlässige Farb-/Zonen-Buttons (automatischer Direct-Modus) und direkte RGB-Eingabe.",
      "Disk Cleanup: Scan-Bericht als Textdatei exportierbar, klarer Hinweis zu endgültigem Löschen und stabilere Duplikat-Ansicht.",
      "Sicherheit: gespeicherte Anmeldung jetzt mit Windows-DPAPI verschlüsselt, strengere Datenbankregeln und geschützte Admin-Funktionen.",
      "Nur noch eine App-Instanz gleichzeitig – ein zweiter Start holt das laufende Fenster nach vorne.",
      "Einstellungen mit Lizenz- und Datenschutz-Hinweisen; Änderungsliste jetzt mit Datum und Uhrzeit."
    ]
  },
  {
    version: "0.5.19",
    date: "17.10.2026 15:25",
    items: [
      "Bugfix: Der Start hängt nicht mehr bei „Initialisiere…“ – das Startfenster blieb gelegentlich stehen und das Hauptfenster öffnete sich nicht.",
      "Ursache war der Splash-Ablauf: Er wurde erst nach dem Anzeigen des Fensters registriert und konnte dadurch übersprungen werden. Er wird jetzt zuverlässig gestartet.",
      "Zusätzlich hat die Update-Prüfung beim Start ein Zeitlimit, damit der Start nie hängen bleibt."
    ]
  },
  {
    version: "0.5.18",
    date: "17.10.2026 14:50",
    items: [
      "Bugfix: Das Installieren von Updates bricht nicht mehr ab und die App startet nicht mehr endlos mit „Neustart erforderlich“ neu.",
      "Ursache war OpenRGB: Es lief im App-Installationsordner und blockierte so das Update. OpenRGB startet jetzt in seinem eigenen Ordner und wird vor dem Update automatisch beendet.",
      "Beleuchtung: OpenRGB wird weiterhin automatisch mit der App gestartet und beim Update sauber neu geladen."
    ]
  },
  {
    version: "0.5.17",
    date: "17.10.2026 14:23",
    items: [
      "Bugfix: Nach dem Akzeptieren der Nutzungsbedingungen startet die App jetzt zuverlässig weiter – Splash- und Hauptfenster werden wieder angezeigt.",
      "Beleuchtung: Geräte mit schwarzer Standardfarbe erhalten automatisch eine sichtbare Farbe, damit „Farbe anwenden“ die LEDs nicht ausschaltet."
    ]
  },
  {
    version: "0.5.16",
    date: "17.10.2026 14:10",
    items: [
      "Beleuchtung: OpenRGB wird jetzt mit aktiviertem SDK-Server gestartet – Geräte (z. B. Corsair-Tastatur) werden erkannt und sind steuerbar.",
      "Läuft OpenRGB bereits ohne SDK-Server, wird es automatisch mit Server neu gestartet.",
      "Deutlich flüssigere App: Messungen und Spielerkennung laufen jetzt im Hintergrund statt im UI-Thread (behebt das Ruckeln)."
    ]
  },
  {
    version: "0.5.15",
    date: "17.10.2026 13:13",
    items: [
      "OpenRGB wird jetzt automatisch mit der App installiert und aktuell gehalten – kein manueller Download mehr nötig.",
      "Beim Start wird geprüft, ob OpenRGB vorhanden und aktuell ist; bei Bedarf wird es von openrgb.org geladen und gestartet.",
      "Der Beleuchtungs-Tab zeigt Installations- und Update-Fortschritt."
    ]
  },
  {
    version: "0.5.14",
    date: "17.10.2026 12:47",
    items: [
      "Beleuchtung (RGB) komplett neu: Gaming Hub spricht jetzt direkt mit dem OpenRGB-SDK – Maus, Tastatur, Lüfter, GPU und Mainboard werden zuverlässig erkannt.",
      "Farbe und Effekt pro Gerät mit den tatsächlich unterstützten Modi.",
      "OpenRGB kann direkt aus der App gestartet werden, inkl. Hinweis zum SDK-Server."
    ]
  },
  {
    version: "0.5.13",
    date: "17.10.2026 11:18",
    items: [
      "Spiele lassen sich wieder starten/stoppen – jede Karte zeigt direkt Starten/Abbrechen, Klick auf das Bild öffnet die Details.",
      "Beleuchtungs-Tab (RGB) wieder im Menü verfügbar.",
      "Laufwerksfilter repariert – ein einzelnes Laufwerk zeigt die Spiele wieder an.",
      "Spiele-Logos in hoher Auflösung (bis 256×256 statt 32×32)."
    ]
  },
  {
    version: "0.5.12",
    date: "17.10.2026 10:54",
    items: [
      "Premium-Codes: Anzeige „um 0 Tage verlängert“ korrigiert – die tatsächlich gutgeschriebene Laufzeit wird jetzt korrekt angezeigt.",
      "Internet-Checker: Verlauf ist jetzt scrollbar – 1 Woche frei, mit Premium 4 Wochen, inkl. Datums-Anzeige im Diagramm.",
      "Passwortschutz pro App: im Spiele-Tab je Eintrag per Regler aktivierbar, Start nur mit Konto-Passwort.",
      "Spiele-Tab neu: Logo/Vorschaubild, aufklappbare Karten, Start- (grün) und Abbrechen-Knopf (blau).",
      "Premium-Status im Profil mit Countdown und Fortschrittsbalken.",
      "RGB-Beleuchtung, Spiel-Icons, GPU-Fix, Disk-Cleanup-Restzeit, Download-Sperre – alles in dieser Version.",
      "Die Neuigkeiten werden ab jetzt mit jedem Update aktuell gehalten."
    ]
  },
  {
    version: "0.5.6",
    date: "11.10.2026 01:27",
    items: [
      "Neuer „Beleuchtung“ Tab: RGB-Steuerung für Maus, Tastatur, Lüfter, GPU, Mainboard via OpenRGB",
      "Premium-Status im Profil: Countdown mit verbleibender Zeit, Fortschrittsbalken",
      "Spiel-Icons in der Bibliothek (automatische Extraktion via System.Drawing)",
      "Internet-Checker: Pfad-Test, 4-Wochen-Verlauf, Router-vs-ISP Einschätzung, Offline-Hinweis",
      "GPU-Auslastung repariert (korrekte WMI-Klasse), Cleanup-ETA immer sichtbar mit 10-Sek-Takt",
      "Downloads: Neueste für alle, ältere 5 per Admin-Toggle sperrbar",
      "Premium-Codes: Laufzeit/Gültig-bis, unendlich wenn leer, stapelt auf bestehendem Premium",
      "Chat/Voice/Video komplett entfernt"
    ]
  },
  {
    version: "0.5.1",
    date: "10.10.2026 23:31",
    items: [
      "Chat: Nachrichten erscheinen jetzt sofort (robustes Senden, Post-Send-Poll, Fehler-Logging).",
      "Internet-Checker (Premium/Admin): visueller Pfad-Test, 4-Wochen-Verlauf, Ausfall-Einschätzung (Router vs. ISP).",
      "Spiele-Icons: Automatische Extraktion & Anzeige in der Bibliothek.",
      "GPU-Auslastung repariert (korrekte WMI-Klasse), Cleanup-ETA immer sichtbar mit 10-Sek-Takt.",
      "Downloads per Admin-Flag sperrbar, Premium-Codes mit Laufzeit/Gültig-bis, Offline-Hinweis in der Nav."
    ]
  },
  {
    version: "0.5.0",
    date: "10.10.2026 22:12",
    items: [
      "Update-Fortschritt beim Start sichtbar – automatische Installation in den Einstellungen wählbar.",
      "System-Ressourcen: aktueller Windows-Benutzer sowie lokale Benutzer & Gruppen (Premium) und Live-GPU.",
      "Alles in einem durchgehenden, scrollbaren Rahmen – übersichtlicher und sparsamer.",
      "Web-Recherche berücksichtigt die Spielversion korrekt (z. B. OMSI 2 statt OMSI 1).",
      "Premium-Codes sind pro Nutzer nur einmal einlösbar; Admins können Premium sperren.",
      "Neues Abzeichen „Premium-Veteran“ nach 3 Monaten Premium.",
      "Disk Cleanup zeigt eine Live-Restzeit auch bei sehr großen Scans.",
      "Frühere Versionen sind auf der Website dauerhaft herunterladbar."
    ]
  },
  {
    version: "0.4.2",
    date: "10.10.2026 20:50",
    items: [
      "Beim Start werden verfügbare Updates automatisch installiert – danach ein Popup: „Neustart erforderlich“.",
      "Gaming Hub startet automatisch mit Windows und läuft leise im Hintergrund (Infobereich).",
      "Moderne Navigationsleiste mit neuem Design.",
      "Spielebibliothek: nur noch Änderungen beim Start (kein Voll-Rescan) + „Vollständiger Scan“-Knopf.",
      "Verlauf: CPU-/RAM-/GPU-/Prozess-Daten alle 5 Minuten als Diagramm (1 Woche, Premium: 4 Wochen).",
      "Premium ohne Kauf: Freischalt-Codes oder über Abzeichen (Bibliothekar, Systemprofi, Aufräumer, Bugreporter).",
      "Haftungsausschluss nach jedem Update mit Lesezeit-Gate · Disk Cleanup mit Live-Restzeit.",
      "Web-Recherche berücksichtigt optional eine Spielversion (z. B. „2.1“)."
    ]
  },
  {
    version: "0.4.1",
    date: "10.10.2026 19:20",
    items: [
      "Geschriebene Nachrichten erscheinen jetzt sofort im Chat.",
      "Anruf & Videoanruf nur noch in Direktnachrichten.",
      "Präzisere Empfehlung im System-Check (Mindest-/Empfehlungswerte bekannter Spiele).",
      "Web-Recherche zu Systemanforderungen verbessert.",
      "Programm-Log für die Fehlersuche – bei Bugreports automatisch angehängt.",
      "Automatische Updates zuverlässiger · F11 für Vollbild · Prozesse beenden · Dark/Light-Design.",
      "Anmeldung auf der Website über Discord."
    ]
  },
  {
    version: "0.4.0",
    date: "10.10.2026 18:07",
    items: [
      "Kommunikation: Direktnachrichten per E-Mail-Adresse, Antworten auf Nachrichten und Dateiversand.",
      "Freundesliste mit Anfragen annehmen/ablehnen sowie Blockieren von Nutzern.",
      "Administratoren können einzelne Funktionen für alle freischalten oder deaktivieren.",
      "System-Ressourcen: Netzwerk- und Festplatten-Auslastung funktionieren jetzt zuverlässig.",
      "Disk Cleanup: Fortschritt in Prozent und Anzeige der benötigten Zeit.",
      "Spielebibliothek nach Kategorien als Reiter filterbar."
    ]
  },
  {
    version: "0.3.1",
    date: "10.10.2026 17:08",
    items: ["Automatisches Update beim Start wurde zuverlässig gemacht."]
  },
  {
    version: "0.3.0",
    date: "10.10.2026 17:03",
    items: [
      "Textchat mit mehreren Kanälen und Direktnachrichten.",
      "Sprachanrufe und Videoanrufe zwischen Nutzern.",
      "Benachrichtigungen mit Ton und Desktop-Hinweisen."
    ]
  }
];

type Page = "start" | "bugtracker" | "admin" | "changelog" | "versionen";

function parseHash(): Page {
  const h = window.location.hash.replace(/^#\/?/, "");
  if (h.startsWith("admin")) return "admin";
  if (h.startsWith("bugs") || h.startsWith("bugtracker")) return "bugtracker";
  if (h.startsWith("versionen")) return "versionen";
  if (h.startsWith("changelog")) return "changelog";
  return "start";
}

export function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [isAdmin, setIsAdmin] = useState(false);
  const [page, setPage] = useState<Page>(parseHash());
  const [downloadsEnabled, setDownloadsEnabled] = useState(true);
  const [releases, setReleases] = useState<ReleaseInfo[]>([]);

  useEffect(() => {
    const onHash = () => setPage(parseHash());
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  useEffect(() => {
    if (!supabase) return;
    const apply = (s: Session | null) => setSession(s);
    supabase.auth.getSession().then(({ data }) => apply(data.session));
    const { data: sub } = supabase.auth.onAuthStateChange((_e, s) => apply(s));
    return () => sub.subscription.unsubscribe();
  }, []);

  useEffect(() => {
    if (session) void isCurrentUserAdmin().then(setIsAdmin);
    else setIsAdmin(false);
  }, [session]);

  useEffect(() => {
    void isDownloadsEnabled().then(setDownloadsEnabled);
  }, []);

  useEffect(() => {
    void fetchReleases().then(setReleases);
  }, []);

  const userEmail = session?.user?.email;

  const signInDiscord = () => {
    void supabase?.auth.signInWithOAuth({
      provider: "discord",
      options: { redirectTo: window.location.origin + window.location.pathname }
    });
  };

  const go = (p: Page) => {
    window.location.hash = p === "start" ? "#/" : `#/${p}`;
    setPage(p);
  };

  return (
    <div className="app">
      <header className="nav">
        <a className="brand" href="#/" onClick={() => go("start")}>
          <span className="logo">GH</span> Gaming Hub
        </a>
        <nav>
          <button className={"navlink" + (page === "start" ? " active" : "")} onClick={() => go("start")}>
            Features
          </button>
          <button className={"navlink" + (page === "changelog" ? " active" : "")} onClick={() => go("changelog")}>
            Änderungsverlauf
          </button>
          <button className={"navlink" + (page === "versionen" ? " active" : "")} onClick={() => go("versionen")}>
            Frühere Versionen
          </button>
          <button className={"navlink" + (page === "bugtracker" ? " active" : "")} onClick={() => go("bugtracker")}>
            Bugtracker
          </button>
          {isAdmin && (
            <button className={"navlink" + (page === "admin" ? " active" : "")} onClick={() => go("admin")}>
              Admin
            </button>
          )}
        </nav>
        <div className="nav-actions">
          {userEmail ? (
            <>
              <span className="user">{userEmail}</span>
              <button className="btn ghost" onClick={() => supabase?.auth.signOut()}>
                Abmelden
              </button>
            </>
          ) : (
            <button className="btn ghost" onClick={signInDiscord}>
              Mit Discord anmelden
            </button>
          )}
        </div>
      </header>

      {page === "start" && (
        <>
          <section className="hero" id="top">
            <div className="hero-glow" />
            <h1>Dein Gaming-Dashboard für Windows</h1>
            <p>
              Spiele verwalten, Speicher aufräumen, System im Blick behalten – und deine Spiele
              komfortabel starten. Von Gamern für Gamer. Kostenlos für Windows.
            </p>
            <div className="hero-actions">
              {(downloadsEnabled || isAdmin) && (
                <>
                  <a className="btn primary" href={DOWNLOAD_SETUP_URL}>Für Windows herunterladen</a>
                  <a className="btn ghost" href={DOWNLOAD_PORTABLE_URL}>Portable (ZIP)</a>
                </>
              )}
              {(!downloadsEnabled && !isAdmin) && (
                <p className="muted small">Downloads sind aktuell deaktiviert.</p>
              )}
            </div>
            <p className="muted small">
              Hinweis: Gaming Hub wird ohne Gewähr bereitgestellt. Nutzung auf eigene Gefahr.
            </p>
            <div className="download-help">
              <h4>Brauchst du Hilfe beim Download?</h4>
              <p>
                Die Installer sind aktuell noch nicht mit einem kostenpflichtigen Zertifikat signiert.
                Browser und Windows zeigen deshalb bewusst eine Warnung. So kommst du sicher trotzdem ran:
              </p>
              <ol>
                <li>
                  <strong>Browser (z.&nbsp;B. Edge/Chrome):</strong> Bei „<em>Datei nicht sicher</em>“ auf
                  „Alternative herunterladen“ bzw. „Trotzdem behalten“ klicken.
                </li>
                <li>
                  <strong>Windows SmartScreen:</strong> „Weitere Informationen“ → „Trotzdem ausführen“.
                  Beim Portable-Ordner zusätzlich im Defender unter „Bedrohungsschutz“ erlauben.
                </li>
                <li>
                  <strong>Empfehlung:</strong> Nutze den <strong>Setup-Installer</strong> statt der
                  Portable-ZIP – die einzelne EXE (selbst-entpackend) wird von Antiviren öfter fälschlich
                  gelöscht. Die Portable-Version gibt es als normalen Ordner, und zwar ohne
                  Selbst-Extrakt. Für mehr Vertrauen: Überprüfe die SHA-256-Checksummen im jeweils
                  verlinkten Release.
                </li>
              </ol>
            </div>
          </section>

          <section className="features" id="features">
            {FEATURES.map((f) => (
              <div className="card" key={f.title}>
                <div className="card-icon">{f.icon}</div>
                <h3>{f.title}</h3>
                <p>{f.text}</p>
              </div>
            ))}
          </section>

          <section className="wide" id="neu" style={{ margin: "30px 0" }}>
            <h2 className="section-title">Zuletzt dazugekommen</h2>
            <div className="changelog">
              {CHANGELOG[0].items.slice(0, 4).map((d, i) => (
                <div className="card changelog-item" key={i}>
                  <span className="changelog-check">✓</span>
                  <div>
                    <h4>Neu</h4>
                    <p>{d}</p>
                  </div>
                </div>
              ))}
            </div>
          </section>
        </>
      )}

      {page === "bugtracker" && (
        <section id="bugs" style={{ marginTop: "24px" }}>
          {!userEmail && (
            <div className="card wide" style={{ marginBottom: "16px" }}>
              <h2>Bugtracker</h2>
              <p className="muted">
                Melde Probleme und verfolge den Bearbeitungsstatus. Melde dich an, um den Status deiner
                Meldungen zu sehen.
              </p>
              {supabase && (
                <button className="btn" onClick={signInDiscord}>Mit Discord anmelden</button>
              )}
            </div>
          )}
          <BugTracker configured={isSupabaseConfigured} onChanged={() => undefined} />
        </section>
      )}

      {page === "admin" && (
        <section id="admin" style={{ marginTop: "24px" }}>
          <Admin />
        </section>
      )}

      {page === "changelog" && (
        <section id="changelog" style={{ marginTop: "24px" }}>
          <h2 className="section-title">Änderungsverlauf</h2>
          <div className="changelog">
            {CHANGELOG.map((c) => (
              <div className="card changelog-item" key={c.version}>
                <span className="changelog-check">◆</span>
                <div>
                  <h4>
                    Version {c.version} <span className="muted small">· {c.date}</span>
                  </h4>
                  <ul className="changelog-list">
                    {c.items.map((it, i) => (
                      <li key={i}>{it}</li>
                    ))}
                  </ul>
                </div>
              </div>
            ))}
          </div>
        </section>
      )}

{page === "versionen" && (
        <section id="versionen" style={{ marginTop: "24px" }}>
          <h2 className="section-title">Frühere Versionen</h2>
          <p className="muted small">
            Die neueste Version ist für alle kostenlos. Ältere Versionen sind hier verfügbar.
          </p>
          {releases.length === 0 ? (
            <div className="card">
              <p className="muted small">
                Die Versionsliste konnte nicht geladen werden.
              </p>
            </div>
          ) : (

            <div className="changelog">
              {releases.map((r, i) => {
                const isLatest = i === 0;
                const canDownload = isLatest || downloadsEnabled || isAdmin;
                return (
                  <div className="card changelog-item" key={r.version}>
                    <span className="changelog-check">⬇</span>
                    <div style={{ flex: 1 }}>
                      <h4>
                        Version {r.version}{" "}
                        {isLatest && <span className="badge-you">aktuell</span>}{" "}
                        <span className="muted small">
                          · {new Date(r.publishedAt).toLocaleString("de-DE", {
                            day: "2-digit",
                            month: "2-digit",
                            year: "numeric",
                            hour: "2-digit",
                            minute: "2-digit"
                          })}
                        </span>
                      </h4>
                      <div className="row" style={{ marginTop: 8 }}>
                        {canDownload ? (
                          <>
                            {r.setupUrl && (
                              <a className="btn tiny" href={r.setupUrl}>
                                Setup (.exe)
                              </a>
                            )}
                            {r.portableUrl && (
                              <a className="btn tiny ghost" href={r.portableUrl}>
                                Portable (.zip)
                              </a>
                            )}
                          </>
                        ) : (
                          <span className="muted small">Downloads deaktiviert</span>
                        )}
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </section>
      )}

      <footer>
        <span>© {new Date().getFullYear()} Jann Genzmann – Gaming Hub · Alle Rechte vorbehalten</span>
        <span>
          <span className="muted small">Lizenz in der App unter Einstellungen</span>
          {" · "}
          <a href="#/" onClick={() => go("start")}>Nach oben</a>
        </span>
      </footer>

    </div>
  );
}

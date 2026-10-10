import { useEffect, useState } from "react";
import type { Session } from "@supabase/supabase-js";
import { supabase, isCurrentUserAdmin, isDownloadsEnabled } from "./supabase";
import { BugTracker } from "./BugTracker";
import { Admin } from "./Admin";
import {
  DOWNLOAD_SETUP_URL,
  DOWNLOAD_PORTABLE_URL,
  RELEASES_PAGE_URL,
  releaseAssetUrl,
  isSupabaseConfigured
} from "./config";

const FEATURES = [
  { icon: "🎮", title: "Spielbibliothek", text: "Steam, Epic und installierte Programme automatisch erkennen, starten, stoppen und Spielzeiten verfolgen." },
  { icon: "🧹", title: "Disk Cleanup", text: "Doppelte Dateien finden und mit einem Klick aufräumen – sicher über den Papierkorb." },
  { icon: "📊", title: "System-Check", text: "Live CPU-, RAM-, Netzwerk- und Festplatten-Auslastung plus Einschätzung, welche Spiele auf deinem PC laufen." },
  { icon: "💬", title: "Chat, Voice & Video", text: "Direkt in der App mit Freunden schreiben und reden – inkl. Direktnachrichten, Antworten und Dateiversand." },
  { icon: "🏆", title: "Abzeichen", text: "Sammle Erfolge, z. B. das Early-Bird-Abzeichen für frühe Nutzer 2026." },
  { icon: "🐞", title: "Bugtracker", text: "Probleme direkt melden und den Bearbeitungsstatus im Blick behalten." }
];

const CHANGELOG: { version: string; date: string; items: string[] }[] = [
  {
    version: "0.5.1",
    date: "Oktober 2026",
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
    date: "Oktober 2026",
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
    date: "Oktober 2026",
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
    date: "Oktober 2026",
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
    date: "Oktober 2026",
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
    date: "Oktober 2026",
    items: ["Automatisches Update beim Start wurde zuverlässig gemacht."]
  },
  {
    version: "0.3.0",
    date: "Oktober 2026",
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
  if (h.startsWith("changelog") || h.startsWith("versionen")) return "changelog";
  return "start";
}

export function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [isAdmin, setIsAdmin] = useState(false);
  const [page, setPage] = useState<Page>(parseHash());
  const [downloadsEnabled, setDownloadsEnabled] = useState(true);

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
              Spiele verwalten, Speicher aufräumen, System im Blick behalten – und mit Freunden reden.
              Von Gamern für Gamer. Kostenlos für Windows.
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
            Die letzten 6 Versionen stehen zum Download bereit. Ältere Versionen finden Sie auf der
            <a href={RELEASES_PAGE_URL} target="_blank" rel="noreferrer">GitHub Releases-Seite</a>.
            Hinweis: Nur die jeweils <strong>neueste Version</strong> erhält automatische Updates.
          </p>
          <div className="changelog">
            {CHANGELOG.slice(0, 6).map((c, i) => (
              <div className="card changelog-item" key={c.version}>
                <span className="changelog-check">⬇</span>
                <div style={{ flex: 1 }}>
                  <h4>
                    Version {c.version}{" "}
                    {i === 0 ? (
                      <span className="badge-you">aktuell</span>
                    ) : (
                      <span className="muted small">· {c.date}</span>
                    )}
                  </h4>
<div className="row" style={{ marginTop: 8 }}>
                      {(downloadsEnabled || isAdmin) && (
                        <>
                          <a className="btn tiny" href={releaseAssetUrl(c.version, "setup")}>
                            Setup (.exe)
                          </a>
                          <a className="btn tiny ghost" href={releaseAssetUrl(c.version, "portable")}>
                            Portable (.zip)
                          </a>
                        </>
                      )}
                      {(!downloadsEnabled && !isAdmin) && (
                        <span className="muted small">Downloads deaktiviert</span>
                      )}
                    </div>
                </div>
              </div>
            ))}
          </div>
          <p className="muted small" style={{ marginTop: 12 }}>
            Ältere Releases, Prüfsummen und alle Dateien findest du auch auf der{" "}
            <a href={RELEASES_PAGE_URL} target="_blank" rel="noreferrer">
              Releases-Seite
            </a>
            .
          </p>
        </section>
      )}

      <footer>
        <span>© {new Date().getFullYear()} Jann Genzmann – Gaming Hub · Alle Rechte vorbehalten</span>
        <span><a href="#/" onClick={() => go("start")}>Nach oben</a></span>
      </footer>
    </div>
  );
}

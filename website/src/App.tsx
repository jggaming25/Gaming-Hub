import { useEffect, useState } from "react";
import type { Session } from "@supabase/supabase-js";
import { supabase, isCurrentUserAdmin } from "./supabase";
import { BugTracker } from "./BugTracker";
import { Admin } from "./Admin";
import { DOWNLOAD_SETUP_URL, DOWNLOAD_PORTABLE_URL, isSupabaseConfigured } from "./config";

const FEATURES = [
  { icon: "🎮", title: "Spielbibliothek", text: "Steam, Epic und installierte Programme automatisch erkennen, starten, stoppen und Spielzeiten verfolgen." },
  { icon: "🧹", title: "Disk Cleanup", text: "Doppelte Dateien finden und mit einem Klick aufräumen – sicher über den Papierkorb." },
  { icon: "📊", title: "System-Check", text: "Live CPU-, RAM- und GPU-Auslastung plus Einschätzung, welche Spiele auf deinem PC laufen." },
  { icon: "💬", title: "Chat, Voice & Video", text: "Direkt in der App mit Freunden reden – Peer-to-Peer ohne Umwege." },
  { icon: "🏆", title: "Abzeichen", text: "Sammle Erfolge, z. B. das Early-Bird-Abzeichen für frühe Nutzer 2026." },
  { icon: "🐞", title: "Bugtracker", text: "Probleme direkt melden und den Bearbeitungsstatus im Blick behalten." }
];

export function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [isAdmin, setIsAdmin] = useState(false);

  useEffect(() => {
    if (!supabase) return;
    const apply = (s: Session | null) => {
      setSession(s);
    };
    supabase.auth.getSession().then(({ data }) => apply(data.session));
    const { data: sub } = supabase.auth.onAuthStateChange((_e, s) => apply(s));
    return () => sub.subscription.unsubscribe();
  }, []);

  useEffect(() => {
    if (session) void isCurrentUserAdmin().then(setIsAdmin);
    else setIsAdmin(false);
  }, [session]);

  const userEmail = session?.user?.email;

  return (
    <div className="app">
      <header className="nav">
        <a className="brand" href="#top">
          <span className="logo">GH</span> Gaming Hub
        </a>
        <nav>
          <a href="#features">Features</a>
          <a href="#neu">Neu</a>
          {isAdmin && <a href="#admin">Admin</a>}
          <a href="#bugs">Bugtracker</a>
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
            <a className="btn" href="#bugs">Anmelden</a>
          )}
        </div>
      </header>

      <section className="hero" id="top">
        <div className="hero-glow" />
        <h1>Dein Gaming-Dashboard für Windows</h1>
        <p>
          Spiele verwalten, Speicher aufräumen, System im Blick behalten – und mit Freunden reden.
          Von Gamern für Gamer. Kostenlos für Windows.
        </p>
        <div className="hero-actions">
          <a className="btn primary" href={DOWNLOAD_SETUP_URL}>Für Windows herunterladen</a>
          <a className="btn ghost" href={DOWNLOAD_PORTABLE_URL}>Portable (ZIP)</a>
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
          <p>
            Meldest du die Datei zusätzlich bei Microsoft, verbessert das mit der Zeit das
            SmartScreen-Reputations-Verfahren:{" "}
            <a href="https://www.microsoft.com/wdsi/filesubmission" target="_blank" rel="noreferrer">
              https://www.microsoft.com/wdsi/filesubmission
            </a>
          </p>
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
          {[
            ["Spiel-Kategorien", "Automatische Einordnung plus Filter – die Liste zeigt standardmäßig nur echte Spiele."],
            ["Steam-Spielzeit-Import", "Bereits vorhandene Spielzeit aus dem Steam-Profil wird übernommen und weitergezählt."],
            ["System-Monitor", "Task-Manager-artige Live-Ansicht: CPU, RAM, Netzwerk, Datenträger und Top-Prozesse mit Verlauf."],
            ["Desktop-Benachrichtigungen", "Neue Nachrichten, klingelnde und verpasste Anrufe erscheinen unten rechts mit Logo und Klingelton."]
          ].map(([t, d]) => (
            <div className="card changelog-item" key={t}>
              <span className="changelog-check">✓</span>
              <div>
                <h4>{t}</h4>
                <p>{d}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

      <section id="bugs">
        <BugTracker configured={isSupabaseConfigured} onChanged={() => undefined} />
      </section>

      {isAdmin && (
        <section id="admin">
          <Admin />
        </section>
      )}

      <footer>
        <span>© {new Date().getFullYear()} Jann Genzmann – Gaming Hub · Alle Rechte vorbehalten</span>
        <span><a href="#top">Nach oben</a></span>
      </footer>
    </div>
  );
}

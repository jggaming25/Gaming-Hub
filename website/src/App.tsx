import { useEffect, useState } from "react";
import type { Session } from "@supabase/supabase-js";
import { supabase } from "./supabase";
import { BugTracker } from "./BugTracker";
import { DOWNLOAD_URL, PORTABLE_URL, REPO_URL, isSupabaseConfigured } from "./config";

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

  useEffect(() => {
    if (!supabase) return;
    supabase.auth.getSession().then(({ data }) => setSession(data.session));
    const { data: sub } = supabase.auth.onAuthStateChange((_e, s) => setSession(s));
    return () => sub.subscription.unsubscribe();
  }, []);

  const userEmail = session?.user?.email;

  return (
    <div className="app">
      <header className="nav">
        <a className="brand" href="#top">
          <span className="logo">GH</span> Gaming Hub
        </a>
        <nav>
          <a href="#features">Features</a>
          <a href="#bugs">Bugtracker</a>
          <a href={REPO_URL} target="_blank" rel="noreferrer">GitHub</a>
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
          Von Gamern für Gamer. Kostenlos und quelloffen.
        </p>
        <div className="hero-actions">
          <a className="btn primary" href={DOWNLOAD_URL}>Für Windows herunterladen</a>
          <a className="btn ghost" href={PORTABLE_URL}>Portable (ZIP)</a>
        </div>
        <p className="muted small">
          Hinweis: Gaming Hub wird ohne Gewähr bereitgestellt. Nutzung auf eigene Gefahr.
        </p>
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

      <section id="bugs">
        <BugTracker configured={isSupabaseConfigured} onChanged={() => undefined} />
      </section>

      <footer>
        <span>© {new Date().getFullYear()} Jann Genzmann – Gaming Hub</span>
        <span>
          <a href={REPO_URL} target="_blank" rel="noreferrer">Quellcode</a> · Alle Rechte vorbehalten
        </span>
      </footer>
    </div>
  );
}

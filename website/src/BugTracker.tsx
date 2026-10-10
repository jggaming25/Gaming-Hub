import { useEffect, useState, type FormEvent } from "react";
import { supabase, fetchBugs, submitBug, updateBugStatus, type Bug, type BugStatus } from "./supabase";

const STATUS_LABEL: Record<BugStatus, string> = {
  open: "Offen",
  in_progress: "In Bearbeitung",
  done: "Erledigt",
  rejected: "Abgelehnt"
};

interface Props {
  configured: boolean;
  onChanged: () => void;
}

export function BugTracker({ configured }: Props) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [authed, setAuthed] = useState(false);
  const [isAdmin, setIsAdmin] = useState(false);
  const [bugs, setBugs] = useState<Bug[]>([]);
  const [status, setStatus] = useState("");

  const [title, setTitle] = useState("");
  const [category, setCategory] = useState("Absturz / Crash");
  const [steps, setSteps] = useState("");

  const refresh = async () => {
    const { bugs: list, isAdmin: admin, error } = await fetchBugs();
    if (error) setStatus(error);
    setBugs(list);
    setIsAdmin(admin);
    const { data } = (await supabase?.auth.getUser()) ?? { data: { user: null } };
    setAuthed(Boolean(data?.user));
  };

  useEffect(() => {
    if (!configured) return;
    void refresh();
    const { data: sub } = supabase?.auth.onAuthStateChange(_e => {
      void refresh();
    }) ?? { data: { subscription: undefined } };
    return () => sub.subscription?.unsubscribe();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [configured]);

  /** Live-Check, ob wirklich noch ein Nutzer angemeldet ist – blockiert sonst den Bugreport. */
  const requireAuth = async (): Promise<boolean> => {
    if (!supabase) return false;
    const { data, error } = await supabase.auth.getUser();
    const ok = !error && Boolean(data?.user);
    setAuthed(ok);
    return ok;
  };

  async function handleAuth(e: FormEvent, mode: "in" | "up") {
    e.preventDefault();
    if (!supabase) return;
    const res =
      mode === "in"
        ? await supabase.auth.signInWithPassword({ email, password })
        : await supabase.auth.signUp({ email, password });
    if (res.error) setStatus(res.error.message);
    else {
      setStatus(mode === "in" ? "Angemeldet." : "Registriert – ggf. E-Mail bestätigen.");
      await refresh();
    }
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const loggedIn = await requireAuth();
    if (!loggedIn) {
      setStatus("Du bist nicht (mehr) angemeldet – bitte melde dich an, um einen Bug zu melden.");
      return;
    }
    if (!title.trim() || !steps.trim()) {
      setStatus("Bitte Titel und Schritte ausfüllen.");
      return;
    }
    const res = await submitBug({ title, category, steps, appVersion: "Website" });
    if (res.ok) {
      setTitle("");
      setSteps("");
      setStatus("Danke! Dein Bugreport wurde übermittelt.");
      await refresh();
    } else {
      setStatus(res.error ?? "Unbekannter Fehler");
    }
  }

  async function changeStatus(bug: Bug, next: BugStatus) {
    const res = await updateBugStatus(bug.id, next);
    if (!res.ok) setStatus(res.error ?? "Fehler");
    await refresh();
  }

  if (!configured) {
    return (
      <div className="card wide">
        <h2>Bugtracker</h2>
        <p className="muted">
          Der Bugtracker wird aktiv, sobald ein Supabase-Projekt verbunden ist
          (<code>VITE_SUPABASE_URL</code> und <code>VITE_SUPABASE_ANON_KEY</code> setzen).
        </p>
      </div>
    );
  }

  return (
    <div className="card wide" id="bugtracker">
      <h2>Bugtracker</h2>

      {!authed ? (
        <form className="auth" onSubmit={(e) => handleAuth(e, "in")}>
          <p className="muted small">Melde dich an, um Bugs zu melden und deinen Status zu verfolgen.</p>
          <input placeholder="E-Mail" value={email} onChange={(e) => setEmail(e.target.value)} />
          <input type="password" placeholder="Passwort" value={password} onChange={(e) => setPassword(e.target.value)} />
          <div className="row">
            <button className="btn primary" type="submit">Anmelden</button>
            <button className="btn ghost" type="button" onClick={(e) => handleAuth(e, "up")}>
              Registrieren
            </button>
          </div>
        </form>
      ) : (
        <>
          <div className="bugmeta-loggedin">
            <span className="badge-admin">✓ Angemeldet</span>
            <span className="muted small">Nur angemeldete Nutzer können Bugs melden – der Status wird live geprüft.</span>
          </div>
          <form className="bugform" onSubmit={handleSubmit}>
            <input placeholder="Kurzer Titel" value={title} onChange={(e) => setTitle(e.target.value)} />
            <select value={category} onChange={(e) => setCategory(e.target.value)}>
              <option>Absturz / Crash</option>
              <option>Feature funktioniert nicht</option>
              <option>Darstellungsfehler</option>
              <option>Startproblem</option>
              <option>Performance</option>
              <option>Sonstiges</option>
            </select>
            <textarea
              placeholder="Schritte zum Reproduzieren"
              rows={4}
              value={steps}
              onChange={(e) => setSteps(e.target.value)}
            />
            <div className="row">
              <button className="btn primary" type="submit">Bugreport senden</button>
              {isAdmin && <span className="badge-admin">Admin</span>}
            </div>
          </form>

          <ul className="buglist">
            {bugs.map((b) => (
              <li key={b.id}>
                <div className="bug-head">
                  <strong>{b.title}</strong>
                  <span className={`status status-${b.status}`}>{STATUS_LABEL[b.status]}</span>
                </div>
                <div className="bug-meta">
                  {b.category} · {new Date(b.created_at).toLocaleString("de-DE")}
                </div>
                <p className="bug-steps">{b.steps}</p>
                {b.admin_reply && <p className="bug-reply">Antwort: {b.admin_reply}</p>}
                {isAdmin && (
                  <div className="row">
                    {(Object.keys(STATUS_LABEL) as BugStatus[]).map((s) => (
                      <button key={s} className="btn tiny" onClick={() => changeStatus(b, s)}>
                        {STATUS_LABEL[s]}
                      </button>
                    ))}
                  </div>
                )}
              </li>
            ))}
            {bugs.length === 0 && <li className="muted">Noch keine Meldungen.</li>}
          </ul>
        </>
      )}

      {status && <p className="muted small">{status}</p>}
    </div>
  );
}

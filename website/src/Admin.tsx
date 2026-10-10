import { useEffect, useState } from "react";
import {
  supabase,
  fetchBugs,
  fetchProfiles,
  setProfileRole,
  setProfileBanned,
  updateBugStatus,
  isCurrentUserAdmin,
  type Bug,
  type BugStatus,
  type Profile
} from "./supabase";

const STATUS_LABEL: Record<BugStatus, string> = {
  open: "Offen",
  in_progress: "In Bearbeitung",
  done: "Erledigt",
  rejected: "Abgelehnt"
};

type Tab = "bugs" | "users";

export function Admin() {
  const [tab, setTab] = useState<Tab>("bugs");
  const [allowed, setAllowed] = useState(false);
  const [me, setMe] = useState<string | null>(null);
  const [bugs, setBugs] = useState<Bug[]>([]);
  const [profiles, setProfiles] = useState<Profile[]>([]);
  const [replyDrafts, setReplyDrafts] = useState<Record<string, string>>({});
  const [status, setStatus] = useState("");

  const refresh = async () => {
    const admin = await isCurrentUserAdmin();
    setAllowed(admin);
    if (!admin) return;
    const { data: userData } = (await supabase?.auth.getUser()) ?? { data: { user: null } };
    setMe(userData.user?.id ?? null);

    const [b, p] = await Promise.all([fetchBugs(), fetchProfiles()]);
    if (b.error) setStatus(b.error);
    if (p.error) setStatus(p.error);
    setBugs(b.bugs);
    setProfiles(p.profiles);
  };

  useEffect(() => {
    void refresh();
    const { data: sub } = supabase?.auth.onAuthStateChange(() => {
      void refresh();
    }) ?? { data: { subscription: undefined } };
    return () => sub.subscription?.unsubscribe();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (!allowed) {
    return (
      <div className="card wide">
        <h2>Admin-Bereich</h2>
        <p className="muted">Zugriff nur für Administratoren.</p>
      </div>
    );
  }

  async function changeStatus(bug: Bug, next: BugStatus) {
    const res = await updateBugStatus(bug.id, next);
    if (!res.ok) setStatus(res.error ?? "Fehler");
    await refresh();
  }

  async function saveReply(bug: Bug) {
    const text = replyDrafts[bug.id]?.trim();
    if (!text) {
      setStatus("Bitte zuerst eine Antwort eintippen.");
      return;
    }
    const res = await updateBugStatus(bug.id, bug.status, text);
    if (!res.ok) setStatus(res.error ?? "Fehler");
    else {
      setStatus("Antwort gespeichert.");
      setReplyDrafts((d) => ({ ...d, [bug.id]: "" }));
      await refresh();
    }
  }

  async function toggleAdmin(p: Profile) {
    const res = await setProfileRole(p.id, !p.is_admin);
    if (!res.ok) setStatus(res.error ?? "Fehler");
    await refresh();
  }

  async function toggleBan(p: Profile) {
    const res = await setProfileBanned(p.id, !p.is_banned);
    if (!res.ok) setStatus(res.error ?? "Fehler");
    await refresh();
  }

  return (
    <div className="card wide">
      <h2>Admin-Bereich</h2>
      <p className="muted small">Steuerung von Bugreports und der Nutzerverwaltung – wie in der App.</p>

      <div className="tabs">
        <button className={"tab-btn" + (tab === "bugs" ? " active" : "")} onClick={() => setTab("bugs")}>
          Bugreports ({bugs.length})
        </button>
        <button className={"tab-btn" + (tab === "users" ? " active" : "")} onClick={() => setTab("users")}>
          Nutzerverwaltung ({profiles.length})
        </button>
      </div>

      {tab === "bugs" && (
        <div className="buglist">
          {bugs.map((b) => (
            <li className="bug-item" key={b.id}>
              <div className="bug-head">
                <strong>{b.title}</strong>
                <span className={`status status-${b.status}`}>{STATUS_LABEL[b.status]}</span>
              </div>
              <div className="bug-meta">
                {b.category} · {b.app_version ? `${b.app_version} · ` : ""}
                {b.reporter_email ?? "Anonym"} · {new Date(b.created_at).toLocaleString("de-DE")}
              </div>
              <p className="bug-steps">{b.steps}</p>
              {b.admin_reply && <p className="bug-reply">Antwort: {b.admin_reply}</p>}

              <div className="row">
                {(Object.keys(STATUS_LABEL) as BugStatus[]).map((s) => (
                  <button key={s} className="btn tiny" onClick={() => changeStatus(b, s)}>
                    {STATUS_LABEL[s]}
                  </button>
                ))}
              </div>

              <div className="return row">
                <input
                  placeholder="Admin-Antwort an den Melder…"
                  value={replyDrafts[b.id] ?? ""}
                  onChange={(e) => setReplyDrafts((d) => ({ ...d, [b.id]: e.target.value }))}
                />
                <button className="btn primary" onClick={() => saveReply(b)}>
                  Antwort speichern
                </button>
              </div>
            </li>
          ))}
          {bugs.length === 0 && <li className="muted">Noch keine Bugreports vorhanden.</li>}
        </div>
      )}

      {tab === "users" && (
        <div className="userlist">
          <div className="user-row user-head">
            <span>E-Mail</span>
            <span>Name</span>
            <span>Registriert</span>
            <span>Status</span>
            <span>Aktionen</span>
          </div>
          {profiles.map((p) => {
            const isMe = p.id === me;
            return (
              <div className="user-row" key={p.id}>
                <span className="user-email">
                  {p.email ?? "–"}
                  {isMe && <span className="badge-you">du</span>}
                </span>
                <span>{p.display_name ?? "–"}</span>
                <span>{new Date(p.created_at).toLocaleDateString("de-DE")}</span>
                <span>
                  {p.is_admin && <span className="badge-admin">Admin</span>}
                  {p.is_banned && <span className="status status-rejected">Gesperrt</span>}
                  {!p.is_admin && !p.is_banned && <span className="muted small">Aktiv</span>}
                </span>
                <span className="row">
                  <button className="btn tiny" disabled={isMe} onClick={() => toggleAdmin(p)}>
                    {p.is_admin ? "Kein Admin" : "Zum Admin"}
                  </button>
                  <button className="btn tiny danger" disabled={isMe} onClick={() => toggleBan(p)}>
                    {p.is_banned ? "Entsperren" : "Sperren"}
                  </button>
                </span>
              </div>
            );
          })}
          {profiles.length === 0 && <p className="muted">Keine Nutzer gefunden.</p>}
        </div>
      )}

      {status && <p className="muted small">{status}</p>}
    </div>
  );
}
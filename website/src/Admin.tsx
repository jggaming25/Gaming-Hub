import { useEffect, useState } from "react";
import {
  supabase,
  fetchBugs,
  fetchProfiles,
  setProfileRole,
  setProfileBanned,
  setProfilePremiumBanned,
  updateBugStatus,
  isCurrentUserAdmin,
  fetchAppSettings,
  setAppSetting,
  fetchPremiumCodes,
  fetchPremiumCodeUses,
  setPremiumCodeDisabled,
  revokePremiumCodeUse,
  type Bug,
  type BugStatus,
  type Profile,
  type PremiumCode,
  type PremiumCodeUse
} from "./supabase";

const STATUS_LABEL: Record<BugStatus, string> = {
  open: "Offen",
  in_progress: "In Bearbeitung",
  done: "Erledigt",
  rejected: "Abgelehnt"
};

type Tab = "bugs" | "users" | "premium";

const FLAG_LABEL: Record<string, string> = {
  premium_codes_enabled: "Freischalt-Codes",
  premium_activity_enabled: "Aktivitäts-Premium (Abzeichen)",
  downloads_enabled: "Downloads (Setup, Portable, alte Versionen)"
};

export function Admin() {
  const [tab, setTab] = useState<Tab>("bugs");
  const [allowed, setAllowed] = useState(false);
  const [me, setMe] = useState<string | null>(null);
  const [bugs, setBugs] = useState<Bug[]>([]);
  const [profiles, setProfiles] = useState<Profile[]>([]);
  const [replyDrafts, setReplyDrafts] = useState<Record<string, string>>({});
  const [status, setStatus] = useState("");

  const [flags, setFlags] = useState<Record<string, boolean>>({});
  const [codes, setCodes] = useState<PremiumCode[]>([]);
  const [codeUses, setCodeUses] = useState<PremiumCodeUse[]>([]);
  const [useSearch, setUseSearch] = useState("");
  const [useFilter, setUseFilter] = useState<"all" | "active" | "revoked">("all");
  const [durationDays, setDurationDays] = useState<number | null>(null);
  const [validUntil, setValidUntil] = useState<string>("");

  const refresh = async () => {
    const admin = await isCurrentUserAdmin();
    setAllowed(admin);
    if (!admin) return;
    const { data: userData } = (await supabase?.auth.getUser()) ?? { data: { user: null } };
    setMe(userData.user?.id ?? null);

    const [b, p, s, c, u] = await Promise.all([
      fetchBugs(),
      fetchProfiles(),
      fetchAppSettings(),
      fetchPremiumCodes(),
      fetchPremiumCodeUses()
    ]);
    if (b.error) setStatus(b.error);
    if (p.error) setStatus(p.error);
    if (s.error) setStatus(s.error);
    if (c.error) setStatus(c.error);
    if (u.error) setStatus(u.error);
    setBugs(b.bugs);
    setProfiles(p.profiles);
    setFlags(Object.fromEntries(s.settings.map((x) => [x.key, x.value])));
    setCodes(c.codes);
    setCodeUses(u.uses);
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

  async function togglePremiumBan(p: Profile) {
    const res = await setProfilePremiumBanned(p.id, !p.premium_banned);
    if (!res.ok) setStatus(res.error ?? "Fehler");
    else setStatus(`Premium für ${p.email ?? p.display_name ?? "Nutzer"} ${p.premium_banned ? "entsperrt" : "gesperrt"}.`);
    await refresh();
  }

  async function toggleFlag(key: string, value: boolean) {
    setFlags((f) => ({ ...f, [key]: value }));
    const res = await setAppSetting(key, value);
    if (!res.ok) setStatus(res.error ?? "Fehler");
    else setStatus(`${FLAG_LABEL[key] ?? key} gespeichert.`);
  }

  async function generateCodes() {
    setStatus("Erstelle Code…");
    const sb = supabase;
    if (!sb) { setStatus("Supabase nicht konfiguriert."); return; }
    const { data: userData } = await sb.auth.getUser();
    const creatorId = userData.user?.id ?? null;
    const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    const rand = () => alphabet[Math.floor(Math.random() * alphabet.length)];
    const part = () => Array.from({ length: 4 }, rand).join("");
    const code = `GH-${part()}-${part()}`;
    const payload: any = { code, created_by: creatorId };
    if (durationDays !== null && durationDays > 0) payload.duration_days = durationDays;
    if (validUntil) payload.valid_until = validUntil;
    const { error } = await sb.from("premium_codes").insert(payload).select("code");
    if (error) { setStatus("Fehler: " + error.message); return; }
    setStatus("Code erstellt.");
    setDurationDays(null);
    setValidUntil("");
    await refresh();
  }

  async function copyUnused() {
    const unused = codes.filter((c) => (c.uses ?? 0) === 0).map((c) => c.code);
    try {
      await navigator.clipboard.writeText(unused.join("\n"));
      setStatus(`${unused.length} Codes kopiert.`);
    } catch {
      setStatus("Zwischenablage nicht verfügbar – bitte manuell kopieren.");
    }
  }

  async function copyCode(code: string) {
    try {
      await navigator.clipboard.writeText(code);
      setStatus(`Code ${code} kopiert.`);
    } catch {
      setStatus("Zwischenablage nicht verfügbar – bitte manuell kopieren.");
    }
  }

  async function toggleCodeDisabled(code: PremiumCode) {
    const res = await setPremiumCodeDisabled(code.id, !code.disabled);
    if (!res.ok) {
      setStatus(res.error ?? "Fehler");
      return;
    }
    setStatus(`Code ${code.code} wurde ${code.disabled ? "wieder aktiviert" : "deaktiviert"}.`);
    await refresh();
  }

  async function disableAllUnused() {
    const unused = codes.filter((c) => (c.uses ?? 0) === 0 && !c.disabled);
    if (unused.length === 0) {
      setStatus("Es gibt keine aktiven, unbenutzten Codes.");
      return;
    }
    if (!window.confirm(`${unused.length} unbenutzte Codes deaktivieren?`)) return;
    for (const code of unused) {
      await setPremiumCodeDisabled(code.id, true);
    }
    setStatus(`${unused.length} unbenutzte Codes deaktiviert.`);
    await refresh();
  }

  async function revokeUse(use: PremiumCodeUse) {
    const who = use.email ?? use.display_name ?? use.user_id;
    if (!window.confirm(
      `Einlösung von „${who}“ für ${use.code} zurücknehmen?\n\n` +
      "Die über diesen Code gutgeschriebene Premium-Zeit wird nach Möglichkeit abgezogen."
    )) return;

    const res = await revokePremiumCodeUse(use.code_id, use.user_id);
    if (!res.ok) {
      setStatus(res.error ?? "Fehler");
      return;
    }
    setStatus("Einlösung zurückgenommen.");
    await refresh();
  }

  function downloadCsv(filename: string, header: string[], rows: (string | number | null | undefined)[][]) {
    const escape = (value: string | number | null | undefined) =>
      `"${String(value ?? "").replace(/"/g, '""')}"`;
    const csv = [header, ...rows].map((row) => row.map(escape).join(";")).join("\r\n");
    const blob = new Blob(["\ufeff" + csv], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
    setStatus(`${filename} exportiert.`);
  }

  function exportCodes() {
    downloadCsv(
      "premium-codes.csv",
      ["Code", "Laufzeit (Tage)", "Gültig bis", "Erstellt", "Einlösungen", "Zuletzt eingelöst", "Status"],
      codes.map((c) => [
        c.code,
        c.duration_days ?? "unendlich",
        c.valid_until ?? "",
        new Date(c.created_at).toLocaleString("de-DE"),
        c.uses ?? 0,
        c.last_used_at ? new Date(c.last_used_at).toLocaleString("de-DE") : "",
        c.disabled ? "deaktiviert" : "aktiv"
      ])
    );
  }

  function exportUses() {
    downloadCsv(
      "premium-code-nutzung.csv",
      ["Code", "Nutzer", "E-Mail", "Eingelöst am", "Status"],
      filteredUses.map((u) => [
        u.code,
        u.display_name ?? "",
        u.email ?? u.user_id,
        new Date(u.used_at).toLocaleString("de-DE"),
        u.revoked_at ? "zurückgenommen" : "aktiv"
      ])
    );
  }

  const search = useSearch.trim().toLowerCase();
  const filteredUses = codeUses.filter((u) => {
    if (useFilter === "active" && u.revoked_at) return false;
    if (useFilter === "revoked" && !u.revoked_at) return false;
    if (!search) return true;
    return `${u.code} ${u.email ?? ""} ${u.display_name ?? ""}`.toLowerCase().includes(search);
  });
  const activeUses = codeUses.filter((u) => !u.revoked_at);
  const revokedUses = codeUses.filter((u) => u.revoked_at);
  const disabledCodes = codes.filter((c) => c.disabled);
  const unusedCodes = codes.filter((c) => (c.uses ?? 0) === 0 && !c.disabled);

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
        <button className={"tab-btn" + (tab === "premium" ? " active" : "")} onClick={() => setTab("premium")}>
          Premium ({codes.length})
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
            const premiumActive = p.premium_until ? new Date(p.premium_until).getTime() > Date.now() : false;
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
                  {premiumActive && (
                    <span className="badge-you">
                      Premium bis {new Date(p.premium_until as string).toLocaleDateString("de-DE")}
                    </span>
                  )}
                  {p.premium_banned && <span className="status status-rejected">Premium gesperrt</span>}
                  {!p.is_admin && !p.is_banned && !premiumActive && !p.premium_banned && (
                    <span className="muted small">Aktiv</span>
                  )}
                </span>
                <span className="row">
                  <button className="btn tiny" disabled={isMe} onClick={() => toggleAdmin(p)}>
                    {p.is_admin ? "Kein Admin" : "Zum Admin"}
                  </button>
                  <button className="btn tiny danger" disabled={isMe} onClick={() => toggleBan(p)}>
                    {p.is_banned ? "Entsperren" : "Sperren"}
                  </button>
                  <button className="btn tiny" disabled={isMe} onClick={() => togglePremiumBan(p)}>
                    {p.premium_banned ? "Premium frei" : "Premium sperren"}
                  </button>
                </span>
              </div>
            );
          })}
          {profiles.length === 0 && <p className="muted">Keine Nutzer gefunden.</p>}
        </div>
      )}

      {tab === "premium" && (
        <div>
          <h3>Premium-Quellen</h3>
          <p className="muted small">
            Premium ist nicht käuflich – es wird über Codes oder über das Erreichen von
            Abzeichen (Aktivität) vergeben. Nutzer erhalten damit u. a. einen
            4-Wochen-Ressourcen-Verlauf statt 1 Woche.
          </p>
          <div className="row wrap">
            {(["premium_codes_enabled", "premium_activity_enabled", "downloads_enabled"] as const).map((key) => (
              <label className="row check-inline" key={key}>
                <input
                  type="checkbox"
                  checked={flags[key] ?? false}
                  onChange={(e) => toggleFlag(key, e.target.checked)}
                />
                {FLAG_LABEL[key]}
              </label>
            ))}
          </div>

          <h3>Freischalt-Codes</h3>
          <p className="muted small">
            Ein Code pro Nutzer einlösbar. Laufzeit (Tage) und/oder Gültig-bis (Datum).
            Beide leer = unendlich. Die Laufzeit verlängert bestehenden Premium-Status nahtlos.
          </p>
          <div className="row">
            <label className="row check-inline">
              Laufzeit (Tage, optional)
              <input className="num tiny" type="number" min={1} max={3650}
                value={durationDays ?? ""} onChange={(e) => setDurationDays(e.target.value ? Number(e.target.value) : null)} />
            </label>
            <label className="row check-inline">
              Gültig bis (optional)
              <input className="num tiny" type="date"
                value={validUntil} onChange={(e) => setValidUntil(e.target.value)} />
            </label>
            <button className="btn primary" onClick={generateCodes}>Code erstellen</button>
            <button className="btn" onClick={copyUnused}>Unbenutzte kopieren</button>
          </div>

          <div className="row wrap" style={{ marginTop: 6 }}>
            <button className="btn" onClick={exportCodes}>Codes als CSV exportieren</button>
            <button className="btn danger" onClick={disableAllUnused}>Alle unbenutzten deaktivieren</button>
          </div>

          <p className="muted small" style={{ marginTop: 8 }}>
            {codes.length} Codes · {unusedCodes.length} unbenutzt aktiv · {disabledCodes.length} deaktiviert ·{" "}
            {activeUses.length} aktive Einlösungen · {revokedUses.length} zurückgenommen
          </p>

          <div className="codelist">
            {codes.length === 0 && <p className="muted small">Noch keine Codes erstellt.</p>}
            {codes.map((c) => (
              <div className="user-row" key={c.id}>
                <span className="code-monospace">
                  {c.code}
                  {c.disabled && <span className="status status-rejected" style={{ marginLeft: 8 }}>deaktiviert</span>}
                </span>
                <span>
                  {c.duration_days ? `${c.duration_days} Tage` : "unendlich"}
                  {c.valid_until && <span className="muted small"> · bis {new Date(c.valid_until).toLocaleDateString("de-DE")}</span>}
                </span>
                <span className="muted small">{new Date(c.created_at).toLocaleDateString("de-DE")}</span>
                <span>
                  {(c.uses ?? 0) > 0 ? (
                    <span className="status status-done">
                      {c.uses}× verwendet
                      {c.last_used_at ? ` · ${new Date(c.last_used_at).toLocaleDateString("de-DE")}` : ""}
                    </span>
                  ) : (
                    <span className="badge-you">Frei</span>
                  )}
                </span>
                <span className="row">
                  <button className="btn tiny" onClick={() => copyCode(c.code)}>Kopieren</button>
                  <button
                    className={"btn tiny" + (c.disabled ? "" : " danger")}
                    onClick={() => toggleCodeDisabled(c)}
                  >
                    {c.disabled ? "Aktivieren" : "Deaktivieren"}
                  </button>
                </span>
              </div>
            ))}
          </div>

          <h3 style={{ marginTop: 26 }}>Einlösungen – wer hat welchen Code genutzt</h3>
          <p className="muted small">
            Zeigt jede Einlösung mit Nutzer und Zeitpunkt. Einzelne Einlösungen lassen sich
            zurücknehmen; die über den Code vergebene Premium-Zeit wird dabei nach Möglichkeit
            abgezogen. Bereits deaktivierte Codes bleiben in dieser Liste zur Nachvollziehbarkeit sichtbar.
          </p>

          <div className="row wrap" style={{ margin: "10px 0" }}>
            <input
              placeholder="Nach Code, Name oder E-Mail filtern…"
              value={useSearch}
              onChange={(e) => setUseSearch(e.target.value)}
              style={{ flex: 1, minWidth: 240 }}
            />
            <select value={useFilter} onChange={(e) => setUseFilter(e.target.value as typeof useFilter)}>
              <option value="all">Alle Einlösungen</option>
              <option value="active">Nur aktive</option>
              <option value="revoked">Nur zurückgenommene</option>
            </select>
            <button className="btn" onClick={exportUses}>Nutzung als CSV exportieren</button>
          </div>

          <div className="uselist">
            <div className="user-row user-head">
              <span>Code</span>
              <span>Nutzer</span>
              <span>Eingelöst am</span>
              <span>Status</span>
              <span>Aktion</span>
            </div>
            {filteredUses.length === 0 && <p className="muted small">Keine Einlösungen gefunden.</p>}
            {filteredUses.map((u) => (
              <div className="user-row" key={`${u.code_id}-${u.user_id}`}>
                <span className="code-monospace">
                  {u.code}
                  {u.disabled && <span className="status status-rejected" style={{ marginLeft: 8 }}>Code deaktiviert</span>}
                </span>
                <span>
                  {u.display_name ?? u.email ?? u.user_id}
                  {u.display_name && u.email && <span className="muted small"> · {u.email}</span>}
                </span>
                <span className="muted small">{new Date(u.used_at).toLocaleString("de-DE")}</span>
                <span>
                  {u.revoked_at ? (
                    <span className="status status-rejected">zurückgenommen</span>
                  ) : (
                    <span className="status status-done">aktiv</span>
                  )}
                </span>
                <span>
                  {u.revoked_at ? (
                    <span className="muted small">–</span>
                  ) : (
                    <button className="btn tiny danger" onClick={() => revokeUse(u)}>Zurücknehmen</button>
                  )}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {status && <p className="muted small">{status}</p>}
    </div>
  );
}
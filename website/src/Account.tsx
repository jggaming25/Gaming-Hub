import { useEffect, useState, type FormEvent } from "react";
import {
  supabase,
  fetchMyPremium,
  redeemPremiumCode,
  type MyPremium,
  type MyPremiumUse
} from "./supabase";

/// <summary>Konto-Seite für normale Nutzer: Premium-Status, Code einlösen und
/// die eigene Einlösungs-Historie – ohne Admin-Rechte.</summary>
export function Account() {
  const [premium, setPremium] = useState<MyPremium | null>(null);
  const [uses, setUses] = useState<MyPremiumUse[]>([]);
  const [code, setCode] = useState("");
  const [status, setStatus] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  const load = async () => {
    setLoading(true);
    const res = await fetchMyPremium();
    if (!res.ok) {
      setStatus(res.error ?? "Premium-Status konnte nicht geladen werden.");
    } else {
      setPremium(res.premium);
      setUses(res.uses);
    }
    setLoading(false);
  };

  useEffect(() => {
    if (!supabase) {
      setLoading(false);
      return;
    }
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const premiumActive = premium
    ? premium.premium_until?.toLowerCase() === "infinity" ||
      (premium.premium_until ? new Date(premium.premium_until).getTime() > Date.now() : false)
    : false;

  async function redeem(event: FormEvent) {
    event.preventDefault();
    if (busy || !code.trim()) return;
    setBusy(true);
    setStatus("Code wird eingelöst…");
    const res = await redeemPremiumCode(code);
    setStatus(res.message);
    if (res.ok) {
      setCode("");
      await load();
    }
    setBusy(false);
  }

  return (
    <section id="konto" style={{ marginTop: 24 }}>
      <h2 className="section-title">Mein Konto</h2>

      <div className="card wide" style={{ marginBottom: 16 }}>
        <h3>Premium-Status</h3>
        {loading ? (
          <p className="muted">Wird geladen…</p>
        ) : (
          <>
            <p>
              {premiumActive
                ? "Dein Premium ist aktiv."
                : "Du hast derzeit kein aktives Premium."}
            </p>
            <p className="muted small">
              {premium?.premium_banned
                ? "Dein Konto ist für Premium gesperrt."
                : premiumActive
                  ? premium?.premium_until?.toLowerCase() === "infinity"
                    ? "Laufzeit: unbegrenzt"
                    : `Gültig bis ${new Date(premium?.premium_until as string).toLocaleString("de-DE")}`
                  : "Premium erhältst du über Freischalt-Codes oder durch Aktivität in der App. Es wird nicht verkauft."}
            </p>
            <p className="muted small">
              Mit Premium: 4 Wochen Ressourcen-Verlauf statt 1 Woche, Treiber- und
              Beleuchtungs-Tab sowie zusätzliche Internet-Checker-Details.
            </p>
          </>
        )}
      </div>

      <div className="card wide" style={{ marginBottom: 16 }}>
        <h3>Freischalt-Code einlösen</h3>
        <p className="muted small">
          Du kannst deinen Code auch hier auf der Website einlösen. Er wird deinem
          angemeldeten Konto gutgeschrieben und verlängert bestehendes Premium nahtlos.
        </p>
        <form className="row" onSubmit={redeem}>
          <input
            value={code}
            onChange={(e) => setCode(e.target.value)}
            placeholder="GH-ABCD-1234"
            style={{ flex: 1, minWidth: 200 }}
            autoComplete="off"
          />
          <button className="btn primary" type="submit" disabled={busy || !code.trim()}>
            {busy ? "Läuft…" : "Einlösen"}
          </button>
        </form>
        {status && <p className="muted small">{status}</p>}
      </div>

      <div className="card wide">
        <h3>Meine eingelösten Codes</h3>
        <p className="muted small">Diese Liste ist nur für dich sichtbar.</p>
        <div className="mylist">
          <div className="user-row user-head">
            <span>Code</span>
            <span>Eingelöst am</span>
            <span>Laufzeit</span>
            <span>Status</span>
          </div>
          {uses.length === 0 && <p className="muted small">Noch keine Codes eingelöst.</p>}
          {uses.map((u, index) => (
            <div className="user-row" key={`${u.code}-${index}`}>
              <span className="code-monospace">{u.code}</span>
              <span className="muted small">{new Date(u.used_at).toLocaleString("de-DE")}</span>
              <span>
                {u.duration_days
                  ? `${u.duration_days} Tage`
                  : u.valid_until
                    ? `bis ${new Date(u.valid_until).toLocaleDateString("de-DE")}`
                    : "unendlich"}
              </span>
              <span>
                {u.revoked_at ? (
                  <span className="status status-rejected">zurückgenommen</span>
                ) : (
                  <span className="status status-done">aktiv</span>
                )}
              </span>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

import { createClient, type SupabaseClient } from "@supabase/supabase-js";
import { SUPABASE_URL, SUPABASE_ANON_KEY, isSupabaseConfigured } from "./config";

export const supabase: SupabaseClient | null = isSupabaseConfigured
  ? createClient(SUPABASE_URL as string, SUPABASE_ANON_KEY as string, {
      auth: {
        flowType: "pkce",
        detectSessionInUrl: true,
        persistSession: true,
        autoRefreshToken: true
      }
    })
  : null;

export type BugStatus = "open" | "in_progress" | "done" | "rejected";

export interface Bug {
  id: string;
  reporter_id: string | null;
  reporter_email: string | null;
  title: string;
  category: string | null;
  steps: string | null;
  app_version: string | null;
  status: BugStatus;
  admin_reply: string | null;
  created_at: string;
}

export async function fetchBugs(): Promise<{ bugs: Bug[]; isAdmin: boolean; error?: string }> {
  if (!supabase) return { bugs: [], isAdmin: false, error: "Supabase ist nicht konfiguriert." };

  const { data: userData } = await supabase.auth.getUser();
  const uid = userData.user?.id;

  let isAdmin = false;
  if (uid) {
    const { data: profile } = await supabase
      .from("profiles")
      .select("is_admin")
      .eq("id", uid)
      .maybeSingle();
    isAdmin = Boolean(profile?.is_admin);
  }

  const { data, error } = await supabase
    .from("bugs")
    .select("*")
    .order("created_at", { ascending: false });

  if (error) return { bugs: [], isAdmin, error: error.message };
  return { bugs: (data as Bug[]) ?? [], isAdmin };
}

export async function submitBug(input: {
  title: string;
  category: string;
  steps: string;
  appVersion: string;
}): Promise<{ ok: boolean; error?: string }> {
  if (!supabase) return { ok: false, error: "Supabase ist nicht konfiguriert." };

  const { data: userData } = await supabase.auth.getUser();
  const user = userData.user;

  const { error } = await supabase.from("bugs").insert({
    title: input.title,
    category: input.category,
    steps: input.steps,
    app_version: input.appVersion,
    reporter_id: user?.id ?? null,
    reporter_email: user?.email ?? null
  });

  return error ? { ok: false, error: error.message } : { ok: true };
}

export async function updateBugStatus(id: string, status: BugStatus, adminReply?: string) {
  if (!supabase) return { ok: false, error: "Supabase ist nicht konfiguriert." };
  const patch: Record<string, unknown> = { status };
  if (adminReply !== undefined) patch.admin_reply = adminReply;
  const { error } = await supabase.from("bugs").update(patch).eq("id", id);
  return error ? { ok: false, error: error.message } : { ok: true };
}

// ---------------------------------------------------------------
// Admin: Nutzerverwaltung
// ---------------------------------------------------------------
export interface Profile {
  id: string;
  email: string | null;
  display_name: string | null;
  avatar_url: string | null;
  is_admin: boolean;
  is_banned: boolean;
  premium_until: string | null;
  premium_since: string | null;
  premium_banned: boolean;
  created_at: string;
}

export async function isCurrentUserAdmin(): Promise<boolean> {
  if (!supabase) return false;
  const { data: userData } = await supabase.auth.getUser();
  const uid = userData.user?.id;
  if (!uid) return false;
  const { data: profile } = await supabase
    .from("profiles")
    .select("is_admin")
    .eq("id", uid)
    .maybeSingle();
  return Boolean(profile?.is_admin);
}

/// <summary>Prüft, ob der angemeldete Nutzer aktives Premium hat (inkl. unbegrenzt).</summary>
export async function isCurrentUserPremium(): Promise<boolean> {
  if (!supabase) return false;
  const { data: userData } = await supabase.auth.getUser();
  const uid = userData.user?.id;
  if (!uid) return false;

  const { data: profile } = await supabase
    .from("profiles")
    .select("premium_until,premium_banned")
    .eq("id", uid)
    .maybeSingle();

  if (!profile || profile.premium_banned) return false;
  const until = profile.premium_until as string | null;
  if (!until) return false;
  if (until.toLowerCase() === "infinity") return true;

  const date = new Date(until);
  return !Number.isNaN(date.getTime()) && date.getTime() > Date.now();
}

export async function fetchProfiles(): Promise<{ profiles: Profile[]; error?: string }> {
  if (!supabase) return { profiles: [], error: "Supabase ist nicht konfiguriert." };
  const { data, error } = await supabase
    .from("profiles")
    .select("*")
    .order("created_at", { ascending: true });
  if (error) return { profiles: [], error: error.message };
  return { profiles: (data as Profile[]) ?? [] };
}

export async function setProfileRole(id: string, isAdmin: boolean) {
  if (!supabase) return { ok: false, error: "Supabase ist nicht konfiguriert." };
  const { error } = await supabase.from("profiles").update({ is_admin: isAdmin }).eq("id", id);
  return error ? { ok: false, error: error.message } : { ok: true };
}

export async function setProfileBanned(id: string, banned: boolean) {
  if (!supabase) return { ok: false, error: "Supabase ist nicht konfiguriert." };
  const { error } = await supabase.from("profiles").update({ is_banned: banned }).eq("id", id);
  return error ? { ok: false, error: error.message } : { ok: true };
}

export async function setProfilePremiumBanned(id: string, banned: boolean) {
  if (!supabase) return { ok: false, error: "Supabase ist nicht konfiguriert." };
  const { error } = await supabase.from("profiles").update({ premium_banned: banned }).eq("id", id);
  return error ? { ok: false, error: error.message } : { ok: true };
}

// ---------------------------------------------------------------
// Admin: Premium (Feature-Flags + Freischalt-Codes)
// ---------------------------------------------------------------
export interface AppSetting {
  key: string;
  value: boolean;
  updated_at?: string;
}

export interface PremiumCode {
  id: string;
  code: string;
  duration_days: number | null;
  valid_until: string | null;
  created_at: string;
  disabled: boolean;
  disabled_at: string | null;
  // Ein Code ist pro Nutzer einmal einlösbar – "uses" zählt die Einlösungen.
  uses: number;
  last_used_at: string | null;
}

export interface PremiumCodeUse {
  code_id: string;
  code: string;
  user_id: string;
  email: string | null;
  display_name: string | null;
  used_at: string;
  revoked_at: string | null;
  duration_days: number | null;
  valid_until: string | null;
  disabled: boolean;
}

export interface MyPremium {
  premium_until: string | null;
  premium_since: string | null;
  premium_banned: boolean;
}

export interface MyPremiumUse {
  code: string;
  used_at: string;
  revoked_at: string | null;
  duration_days: number | null;
  valid_until: string | null;
}

export async function fetchAppSettings(): Promise<{ settings: AppSetting[]; error?: string }> {
  if (!supabase) return { settings: [], error: "Supabase ist nicht konfiguriert." };
  const { data, error } = await supabase.from("app_settings").select("*").order("key");
  if (error) return { settings: [], error: error.message };
  return { settings: (data as AppSetting[]) ?? [] };
}

export async function setAppSetting(key: string, value: boolean) {
  if (!supabase) return { ok: false, error: "Supabase ist nicht konfiguriert." };
  const { error } = await supabase.from("app_settings").update({ value }).eq("key", key);
  return error ? { ok: false, error: error.message } : { ok: true };
}

export async function isDownloadsEnabled(): Promise<boolean> {
  if (!supabase) return true;
  const { data } = await supabase
    .from("app_settings")
    .select("value")
    .eq("key", "downloads_enabled")
    .maybeSingle();
  return data?.value ?? true;
}

export async function fetchPremiumCodes(): Promise<{ codes: PremiumCode[]; error?: string }> {
  if (!supabase) return { codes: [], error: "Supabase ist nicht konfiguriert." };
  // Über eine Serverfunktion, da Codes inkl. Einlösungs-Zähler nur Admin-Only sind.
  const { data, error } = await supabase.rpc("fetch_premium_codes");
  if (error) return { codes: [], error: error.message };
  return { codes: (data as PremiumCode[]) ?? [] };
}

export async function fetchPremiumCodeUses(): Promise<{ uses: PremiumCodeUse[]; error?: string }> {
  if (!supabase) return { uses: [], error: "Supabase ist nicht konfiguriert." };
  const { data, error } = await supabase.rpc("fetch_premium_code_uses");
  if (error) return { uses: [], error: error.message };
  return { uses: (data as PremiumCodeUse[]) ?? [] };
}

export async function setPremiumCodeDisabled(codeId: string, disabled: boolean) {
  if (!supabase) return { ok: false, error: "Supabase ist nicht konfiguriert." };
  const { data, error } = await supabase.rpc("admin_set_premium_code_disabled", {
    p_code_id: codeId,
    p_disabled: disabled
  });
  if (error) return { ok: false, error: error.message };
  const result = data as { ok?: boolean; error?: string } | null;
  return result?.ok ? { ok: true } : { ok: false, error: result?.error ?? "Unbekannter Fehler" };
}

export async function revokePremiumCodeUse(codeId: string, userId: string) {
  if (!supabase) return { ok: false, error: "Supabase ist nicht konfiguriert." };
  const { data, error } = await supabase.rpc("admin_revoke_premium_code_use", {
    p_code_id: codeId,
    p_user_id: userId
  });
  if (error) return { ok: false, error: error.message };
  const result = data as { ok?: boolean; error?: string } | null;
  return result?.ok ? { ok: true } : { ok: false, error: result?.error ?? "Unbekannter Fehler" };
}

/// <summary>Lädt Premium-Status und eigene Code-Einlösungen des angemeldeten Nutzers.</summary>
export async function fetchMyPremium(): Promise<{
  ok: boolean;
  premium: MyPremium | null;
  uses: MyPremiumUse[];
  error?: string;
}> {
  if (!supabase) return { ok: false, premium: null, uses: [], error: "Supabase ist nicht konfiguriert." };
  const { data, error } = await supabase.rpc("fetch_my_premium_history");
  if (error) return { ok: false, premium: null, uses: [], error: error.message };
  const result = data as {
    ok?: boolean;
    premium?: MyPremium;
    uses?: MyPremiumUse[];
    error?: string;
  } | null;
  if (!result?.ok) return { ok: false, premium: null, uses: [], error: result?.error ?? "Unbekannter Fehler" };
  return { ok: true, premium: result.premium ?? null, uses: result.uses ?? [] };
}

const REDEEM_ERRORS: Record<string, string> = {
  login_required: "Bitte zuerst anmelden.",
  premium_banned: "Dein Konto ist für Premium gesperrt.",
  codes_disabled: "Freischalt-Codes sind derzeit deaktiviert.",
  invalid_code: "Dieser Code ist ungültig.",
  already_used: "Du hast diesen Code bereits eingelöst.",
  code_disabled: "Dieser Code wurde deaktiviert."
};

/// <summary>Löst einen Freischalt-Code für den angemeldeten Nutzer ein.</summary>
export async function redeemPremiumCode(code: string): Promise<{
  ok: boolean;
  message: string;
  days?: number | null;
}> {
  if (!supabase) return { ok: false, message: "Supabase ist nicht konfiguriert." };
  const trimmed = code.trim();
  if (!trimmed) return { ok: false, message: "Bitte einen Code eingeben." };

  try {
    const { data, error } = await supabase.rpc("redeem_premium_code", { p_code: trimmed });
    if (error) return { ok: false, message: error.message };
    const result = data as {
      ok?: boolean;
      error?: string;
      days_added?: number | null;
    } | null;
    if (!result?.ok) {
      const key = result?.error ?? "";
      return { ok: false, message: REDEEM_ERRORS[key] ?? key ?? "Code konnte nicht eingelöst werden." };
    }
    if (result.days_added === null || result.days_added === undefined) {
      return { ok: true, message: "Premium dauerhaft aktiviert. Danke!", days: null };
    }
    return {
      ok: true,
      message: `Premium um ${result.days_added} ${result.days_added === 1 ? "Tag" : "Tage"} verlängert. Danke!`,
      days: result.days_added
    };
  } catch (ex) {
    return { ok: false, message: "Fehler beim Verbinden mit dem Server: " + (ex as Error).message };
  }
}

function makePremiumCode(): string {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const rand = () => alphabet[Math.floor(Math.random() * alphabet.length)];
  const part = () => Array.from({ length: 4 }, rand).join("");
  return `GH-${part()}-${part()}`;
}

export async function createPremiumCodes(count: number, bonusDays: number) {
  if (!supabase) return { ok: false, error: "Supabase ist nicht konfiguriert." };
  const { data: userData } = await supabase.auth.getUser();
  const creatorId = userData.user?.id ?? null;
  const rows = Array.from({ length: Math.max(1, Math.min(count, 50)) }, () => ({
    code: makePremiumCode(),
    duration_days: Math.max(1, bonusDays),
    created_by: creatorId
  }));
  const { data, error } = await supabase.from("premium_codes").insert(rows).select("code");
  if (error) return { ok: false, error: error.message };
  return { ok: true, codes: ((data ?? []) as { code: string }[]).map((r) => r.code) };
}

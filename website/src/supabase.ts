import { createClient, type SupabaseClient } from "@supabase/supabase-js";
import { SUPABASE_URL, SUPABASE_ANON_KEY, isSupabaseConfigured } from "./config";

export const supabase: SupabaseClient | null = isSupabaseConfigured
  ? createClient(SUPABASE_URL as string, SUPABASE_ANON_KEY as string)
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

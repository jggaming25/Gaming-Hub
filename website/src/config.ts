export const REPO_URL = "https://github.com/jggaming25/Gaming-Hub";
export const RELEASES_URL = `${REPO_URL}/releases/latest`;

// Downloads werden direkt von dieser Website (GitHub Pages) serviert –
// die Installer liegen unter /Gaming-Hub/download/ und werden durch den
// Website-Workflow automatisch aus dem neuesten Release übernommen.
const BASE = import.meta.env.BASE_URL;
export const DOWNLOAD_URL = `${BASE}download/GamingHub-win-Setup.exe`;
export const PORTABLE_URL = `${BASE}download/GamingHub-win-Portable.zip`;

// Fest hinterlegter öffentlicher Zugang (publishable/anon Key). Wird durch RLS abgesichert.
const DEFAULT_SUPABASE_URL = "https://swrmlwmfyeyztargwsbs.supabase.co";
const DEFAULT_SUPABASE_ANON_KEY = "sb_publishable_PRV4J2sn5FKuJHOul_XaPQ_gzqek4QF";

export const SUPABASE_URL = (import.meta.env.VITE_SUPABASE_URL as string | undefined) || DEFAULT_SUPABASE_URL;
export const SUPABASE_ANON_KEY =
  (import.meta.env.VITE_SUPABASE_ANON_KEY as string | undefined) || DEFAULT_SUPABASE_ANON_KEY;

export const isSupabaseConfigured = Boolean(SUPABASE_URL && SUPABASE_ANON_KEY);

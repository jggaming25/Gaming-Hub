// Source code stays private ("Alle Rechte vorbehalten") – no repository link on the site.
// Downloads come from the official release assets.
export const DOWNLOAD_SETUP_URL =
  "https://github.com/jggaming25/Gaming-Hub/releases/latest/download/GamingHub-win-Setup.exe";
export const DOWNLOAD_PORTABLE_URL =
  "https://github.com/jggaming25/Gaming-Hub/releases/latest/download/GamingHub-win-Portable.zip";

// Alle Releases (auch ältere Versionen) bleiben dauerhaft herunterladbar.
export const RELEASES_PAGE_URL = "https://github.com/jggaming25/Gaming-Hub/releases";

export function releaseAssetUrl(version: string, kind: "setup" | "portable"): string {
  const file = kind === "setup" ? "GamingHub-win-Setup.exe" : "GamingHub-win-Portable.zip";
  return `https://github.com/jggaming25/Gaming-Hub/releases/download/v${version}/${file}`;
}

export interface ReleaseInfo {
  version: string;
  name: string;
  publishedAt: string;
  htmlUrl: string;
  setupUrl: string | null;
  portableUrl: string | null;
}

/// <summary>Lädt die tatsächlich veröffentlichten Versionen inkl. echter
/// Download-Links von der GitHub-API – so werden nur vorhandene Versionen
/// angezeigt (keine toten Links).</summary>
export async function fetchReleases(): Promise<ReleaseInfo[]> {
  try {
    const res = await fetch(
      "https://api.github.com/repos/jggaming25/Gaming-Hub/releases?per_page=30",
      { headers: { Accept: "application/vnd.github+json" } }
    );
    if (!res.ok) return [];

    const data = (await res.json()) as Array<{
      tag_name: string;
      name: string;
      published_at: string;
      html_url: string;
      draft: boolean;
      prerelease: boolean;
      assets: Array<{ name: string; browser_download_url: string }>;
    }>;

    return data
      .filter((r) => !r.draft && !r.prerelease)
      .map((r) => {
        const assets = r.assets ?? [];
        const setup = assets.find((a) => a.name.toLowerCase().endsWith("setup.exe"));
        const portable = assets.find((a) => a.name.toLowerCase().endsWith("portable.zip"));
        return {
          version: r.tag_name.replace(/^v/, ""),
          name: r.name,
          publishedAt: r.published_at,
          htmlUrl: r.html_url,
          setupUrl: setup?.browser_download_url ?? null,
          portableUrl: portable?.browser_download_url ?? null
        };
      });
  } catch {
    return [];
  }
}

// Fest hinterlegter öffentlicher Zugang (publishable/anon Key). Wird durch RLS abgesichert.
const DEFAULT_SUPABASE_URL = "https://swrmlwmfyeyztargwsbs.supabase.co";
const DEFAULT_SUPABASE_ANON_KEY = "sb_publishable_PRV4J2sn5FKuJHOul_XaPQ_gzqek4QF";

export const SUPABASE_URL = (import.meta.env.VITE_SUPABASE_URL as string | undefined) || DEFAULT_SUPABASE_URL;
export const SUPABASE_ANON_KEY =
  (import.meta.env.VITE_SUPABASE_ANON_KEY as string | undefined) || DEFAULT_SUPABASE_ANON_KEY;

export const isSupabaseConfigured = Boolean(SUPABASE_URL && SUPABASE_ANON_KEY);

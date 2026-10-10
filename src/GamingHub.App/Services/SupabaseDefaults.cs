namespace GamingHub.App.Services;

/// <summary>
/// Fest hinterlegte Supabase-Zugangsdaten. Der publishable (anon) Key ist ein
/// öffentlicher Schlüssel und darf im Client stehen; der Zugriff wird durch die
/// RLS-Regeln in supabase/schema.sql abgesichert.
/// </summary>
public static class SupabaseDefaults
{
    public const string Url = "https://swrmlwmfyeyztargwsbs.supabase.co";
    public const string AnonKey = "sb_publishable_PRV4J2sn5FKuJHOul_XaPQ_gzqek4QF";
}

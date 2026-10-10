# Gaming Hub – Supabase Einrichtung

Diese Anleitung aktiviert Login, Abzeichen, Bugtracker und (später) Chat/Voice.

## 1. Projekt anlegen
1. Auf <https://supabase.com> kostenlos registrieren (keine Kreditkarte nötig).
2. **New project** → Name, Passwort, Region (z. B. Frankfurt) wählen.

## 2. Datenbank-Schema
1. Im Dashboard links auf **SQL Editor** → **New query**.
2. Inhalt von [`schema.sql`](schema.sql) einfügen und **Run**.

Enthält Tabellen `profiles`, `badges`, `bugs`, `bug_comments`, Row Level Security und die Admin-Funktion.

## 3. Zugangsdaten eintragen
Unter **Project Settings → API**:
- **Project URL** kopieren.
- **anon public** Key kopieren.

Diese beiden Werte in Gaming Hub unter **Einstellungen → Konto & Cloud (Supabase)** einfügen und **Speichern**. Danach Gaming Hub neu starten.

Alternativ als Datei `supabase.config.json` neben `GamingHub.App.exe`:

```json
{ "url": "https://DEINPROJEKT.supabase.co", "anonKey": "DEIN_ANON_KEY" }
```

> Der anon-Key ist ein **öffentlicher** Schlüssel und darf im Client liegen. Er wird durch RLS abgesichert. Den `service_role`-Key **niemals** in den Client/Repo legen.

## 4. E-Mail-Login aktivieren
**Authentication → Providers → Email** ist standardmäßig aktiv. Optional „Confirm email" abschalten, um sofort loszulegen.

## 5. Google / Discord Login (OAuth)
1. **Authentication → URL Configuration → Redirect URLs** hinzufügen:
   `http://localhost:51555/callback`
2. Bei Google Cloud bzw. Discord Developer Portal eine OAuth-App anlegen und als Redirect-URL
   `https://DEINPROJEKT.supabase.co/auth/v1/callback` eintragen.
3. Client-ID/Secret in **Authentication → Providers → Google/Discord** einfügen und aktivieren.

## 6. Admin für Bugtracker
Nachdem du dich einmal in der App registriert hast:

```sql
update public.profiles set is_admin = true where email = 'deine-admin@mail.de';
```

Admins sehen im Website-Bugtracker alle Meldungen und können Status/Kommentare pflegen.

## Tabellen-Überblick
| Tabelle | Zweck |
| --- | --- |
| `profiles` | Nutzerdaten + Admin-Flag |
| `badges` | Verdiente Abzeichen je Nutzer |
| `bugs` | Bugreports (Status: open / in_progress / done / rejected) |
| `bug_comments` | Kommentar-Verlauf zu Bugs, inkl. Admin-Antworten |

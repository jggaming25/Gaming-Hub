# Gaming Hub – Website

Showcase-Seite mit Download, Feature-Übersicht und Bugtracker (gleicher Login wie die App).

## Lokal starten

```bash
cd website
npm install
cp .env.example .env        # Werte aus Supabase eintragen
npm run dev
```

## Build

```bash
npm run build      # erzeugt website/dist
npm run preview
```

## Konfiguration

Die Seite liest zwei Umgebungsvariablen (siehe `.env.example`):

| Variable | Beschreibung |
| --- | --- |
| `VITE_SUPABASE_URL` | Supabase Project URL |
| `VITE_SUPABASE_ANON_KEY` | öffentlicher anon-Key |

In GitHub unter **Settings → Secrets and variables → Actions** als Secrets hinterlegen, damit der
Deploy-Workflow (`.github/workflows/website.yml`) sie beim Build nutzen kann.

## Deployment (GitHub Pages)

Der Workflow baut die Seite und deployed sie automatisch nach GitHub Pages.

Einmalig nötig: **Settings → Pages → Build and deployment → Source: GitHub Actions**.

Die Seite ist danach erreichbar unter `https://jggaming25.github.io/Gaming-Hub/`
(der `base`-Pfad ist in `vite.config.ts` auf `/Gaming-Hub/` gesetzt).

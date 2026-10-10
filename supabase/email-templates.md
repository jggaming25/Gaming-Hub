# E-Mail-Vorlagen für Supabase (Auth)

Schöne, dunkle Gaming-Hub-Auth-Mails + der Fix für den „404 von GitHub"-Link beim Registrieren.

## 1) URL-Konfiguration (Pflicht – behebt den 404-Link beim Registrieren)

Supabase Dashboard → **Authentication → URL Configuration**:

| Feld | Wert |
|---|---|
| Site URL | `https://swrmlwmfyetyargwsbs.supabase.co` |
| Redirect URLs | `http://localhost:51555/callback` |

Erst dadurch zeigt `{{ .ConfirmationURL }}` in der Mail korrekt auf das Supabase-Projekt
(`https://swrmlwmfyetyargwsbs.supabase.co/auth/v1/verify?…`) statt auf eine GitHub-Pages-Adresse,
die mit „404 not found" endet.

## 2) Templates einfügen

Supabase Dashboard → **Authentication → Emails → Templates** → gewünschtes Template öffnen
(„Confirm signup", „Magic link", „Reset password") → **„Customize template"** aktivieren →
folgendes HTML einfügen. In „Confirm signup" und „Magic link" die Variable
`{{ .ConfirmationURL }}` als Button-Ziel verwenden (im Template unten bereits gesetzt).

```html
<!doctype html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Gaming Hub</title>
</head>
<body style="margin:0;padding:0;background:#0B0D1A;-webkit-text-size-adjust:100%;font-family:Arial,Helvetica,sans-serif;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#0B0D1A;">
  <tr>
    <td align="center" style="padding:32px 16px;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#131730;border-radius:20px;overflow:hidden;">
        <!-- Kopf -->
        <tr>
          <td align="center" style="padding:36px 32px 12px;">
            <table role="presentation" cellpadding="0" cellspacing="0">
              <tr>
                <td align="center"
                    width="56" height="56"
                    style="width:56px;height:56px;border-radius:14px;
                           background:linear-gradient(135deg,#7C3AED,#3B82F6);color:#FFFFFF;
                           font-size:22px;font-weight:800;letter-spacing:1px;">GH</td>
              </tr>
            </table>
            <p style="margin:14px 0 0;color:#E5E7EB;font-size:20px;font-weight:700;">Gaming Hub</p>
            <p style="margin:4px 0 0;color:#9CA3AF;font-size:13px;">Von Gamern für Gamer</p>
          </td>
        </tr>
        <!-- Text -->
        <tr>
          <td align="center" style="padding:24px 40px 8px;color:#E5E7EB;font-size:15px;line-height:1.6;">
            Hallo {{ .Email }}!<br>
            fast geschafft – bestätige deine E-Mail-Adresse,<br>
            damit dein Gaming-Hub-Konto aktiviert wird.
          </td>
        </tr>
        <!-- Button -->
        <tr>
          <td align="center" style="padding:22px 40px 10px;">
            <table role="presentation" cellpadding="0" cellspacing="0">
              <tr>
                <td align="center" style="border-radius:12px;background:linear-gradient(135deg,#7C3AED,#3B82F6);">
                  <a href="{{ .ConfirmationURL }}" target="_blank"
                     style="display:inline-block;padding:14px 36px;color:#FFFFFF;font-size:15px;font-weight:700;
                            text-decoration:none;border-radius:12px;text-shadow:0 1px 2px rgba(0,0,0,0.35);">
                    E-Mail bestätigen
                  </a>
                </td>
              </tr>
            </table>
          </td>
        </tr>
        <!-- Hinweis -->
        <tr>
          <td align="center" style="padding:6px 40px 28px;color:#6B7280;font-size:12px;line-height:1.6;">
            Funktioniert der Button nicht? <a href="{{ .ConfirmationURL }}" style="color:#7C3AED;text-decoration:underline;">Diesen Link öffnen</a>.
            <br>Der Link ist <strong>30&nbsp;Minuten</strong> gültig. Wenn du dich nicht registriert hast, ignoriere diese Mail.
          </td>
        </tr>
        <!-- Footer -->
        <tr>
          <td align="center" style="background:#0E1126;padding:18px 24px;color:#6B7280;font-size:11px;line-height:1.7;">
            © 2026 Jann Genzmann – Gaming Hub · Alle Rechte vorbehalten<br>
            Am Schraderhof 44 · j.genzmann@md-netz.de
          </td>
        </tr>
      </table>
    </td>
  </tr>
</table>
</body>
</html>
```

### Anpassungen für andere Templates

- **Magic link / Einmal-Login:** Text auf „…um dich anzumelden" anpassen (`{{ .ConfirmationURL }}`).
- **Reset password:** Text auf „…um dein Passwort zurückzusetzen" anpassen (`{{ .ConfirmationURL }}`).
- **Change email:** Button „Neue E-Mail bestätigen" (`{{ .ConfirmationURL }}`), Titel „E-Mail-Adresse ändern".
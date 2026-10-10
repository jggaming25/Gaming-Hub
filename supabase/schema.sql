-- Gaming Hub – Supabase Schema, RLS und Admin-Rolle
-- Einfach im Supabase-Dashboard unter "SQL Editor" einfügen und ausführen.

-- ============================================================
-- Erweiterungen
-- ============================================================
create extension if not exists "pgcrypto";

-- ============================================================
-- Profil (1:1 zu auth.users)
-- ============================================================
create table if not exists public.profiles (
    id           uuid primary key references auth.users(id) on delete cascade,
    email        text,
    display_name text,
    avatar_url   text,
    is_admin     boolean not null default false,
    is_banned    boolean not null default false,
    created_at   timestamptz not null default now()
);

-- Nachrüstbar für Bestandsdatenbanken: Spalte fehlt dort noch
alter table public.profiles add column if not exists is_banned boolean not null default false;

-- Profil automatisch beim Registrieren anlegen
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer set search_path = public
as $$
begin
    insert into public.profiles (id, email, display_name, avatar_url)
    values (
        new.id,
        new.email,
        coalesce(new.raw_user_meta_data->>'full_name', new.raw_user_meta_data->>'name', new.email),
        new.raw_user_meta_data->>'avatar_url'
    )
    on conflict (id) do nothing;
    return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
    after insert on auth.users
    for each row execute function public.handle_new_user();

-- Hilfsfunktion: ist der aktuelle Nutzer Admin?
create or replace function public.is_admin()
returns boolean
language sql
security definer set search_path = public
stable
as $$
    select coalesce((select is_admin from public.profiles where id = auth.uid()), false);
$$;

-- ============================================================
-- Abzeichen
-- ============================================================
create table if not exists public.badges (
    user_id    uuid not null references auth.users(id) on delete cascade,
    badge_id   text not null,
    earned_at  timestamptz not null default now(),
    primary key (user_id, badge_id)
);

-- ============================================================
-- Bugtracker
-- ============================================================
create table if not exists public.bugs (
    id             uuid primary key default gen_random_uuid(),
    reporter_id    uuid references auth.users(id) on delete set null,
    reporter_email text,
    title          text not null,
    category       text,
    steps          text,
    app_version    text,
    log_text       text,            -- Programm-Log (automatisch angehängt)
    status         text not null default 'open',   -- open | in_progress | done | rejected
    admin_reply    text,
    created_at     timestamptz not null default now(),
    updated_at     timestamptz not null default now()
);

-- Nachträglich für bestehende Installationen:
alter table public.bugs add column if not exists log_text text;

create table if not exists public.bug_comments (
    id          uuid primary key default gen_random_uuid(),
    bug_id      uuid not null references public.bugs(id) on delete cascade,
    author_id   uuid references auth.users(id) on delete set null,
    author_name text,
    is_admin    boolean not null default false,
    body        text not null,
    created_at  timestamptz not null default now()
);

create or replace function public.touch_updated_at()
returns trigger language plpgsql as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

drop trigger if exists bugs_touch_updated_at on public.bugs;
create trigger bugs_touch_updated_at
    before update on public.bugs
    for each row execute function public.touch_updated_at();

-- ============================================================
-- Row Level Security
-- ============================================================
alter table public.profiles     enable row level security;
alter table public.badges       enable row level security;
alter table public.bugs         enable row level security;
alter table public.bug_comments enable row level security;

-- profiles: jeder Angemeldete darf lesen (für Namen/Avatare), nur eigenes Profil ändern
drop policy if exists "profiles_select" on public.profiles;
create policy "profiles_select" on public.profiles
    for select using (true);

drop policy if exists "profiles_update_own" on public.profiles;
create policy "profiles_update_own" on public.profiles
    for update using (auth.uid() = id) with check (auth.uid() = id);

-- Admin: darf jedes Profil ändern (Admin-Status, Sperrung) -> Nutzerverwaltung
drop policy if exists "profiles_update_admin" on public.profiles;
create policy "profiles_update_admin" on public.profiles
    for update using (public.is_admin()) with check (public.is_admin());

-- Gesperrte Nutzer dürfen keine neuen Bugs melden
drop policy if exists "bugs_insert" on public.bugs;
create policy "bugs_insert" on public.bugs
    for insert with check (
        case
            when auth.uid() is null then true
            else coalesce((select not coalesce(is_banned, false) from public.profiles where id = auth.uid()), true)
        end
    );

-- badges: für alle lesbar, eigene Abzeichen eintragen
drop policy if exists "badges_select" on public.badges;
create policy "badges_select" on public.badges
    for select using (true);

drop policy if exists "badges_insert_own" on public.badges;
create policy "badges_insert_own" on public.badges
    for insert with check (auth.uid() = user_id);

-- bugs: eigene + (Admin: alle) lesen; Insert-Regel siehe oben (Gesperrte dürfen nicht melden)
drop policy if exists "bugs_select" on public.bugs;
create policy "bugs_select" on public.bugs
    for select using (public.is_admin() or auth.uid() = reporter_id);

drop policy if exists "bugs_update_admin" on public.bugs;
create policy "bugs_update_admin" on public.bugs
    for update using (public.is_admin()) with check (public.is_admin());

drop policy if exists "bugs_delete_admin" on public.bugs;
create policy "bugs_delete_admin" on public.bugs
    for delete using (public.is_admin());

-- bug_comments: Angemeldete dürfen kommentieren; sichtbar wenn Bug sichtbar
drop policy if exists "bug_comments_select" on public.bug_comments;
create policy "bug_comments_select" on public.bug_comments
    for select using (
        public.is_admin()
        or exists (select 1 from public.bugs b where b.id = bug_id and b.reporter_id = auth.uid())
    );

drop policy if exists "bug_comments_insert" on public.bug_comments;
create policy "bug_comments_insert" on public.bug_comments
    for insert with check (
        auth.uid() = author_id
        and (
            public.is_admin()
            or exists (select 1 from public.bugs b where b.id = bug_id and b.reporter_id = auth.uid())
        )
    );

-- ============================================================
-- Ersten Admin festlegen (E-Mail anpassen und ausführen)
-- ============================================================
-- update public.profiles set is_admin = true where email = 'deine-admin@mail.de';

-- ============================================================
-- Chat & Anrufe (Phase 6): Nachrichten, Präsenz, WebRTC-Signaling
-- ============================================================

-- Präsenz: letzte Aktivität für den Online-Status
alter table public.profiles add column if not exists last_seen timestamptz;

-- Öffentliche Chatkanäle. Direktnachrichten nutzen eine dynamische
-- Kanal-ID der Form "dm-<uidA>-<uidB>" (kein Eintrag nötig).
create table if not exists public.chat_channels (
    id         text primary key,
    name       text not null,
    is_direct  boolean not null default false,
    created_at timestamptz not null default now()
);

insert into public.chat_channels (id, name, is_direct)
values ('allgemein', 'Allgemein', false)
on conflict (id) do nothing;

create table if not exists public.chat_messages (
    id          uuid primary key default gen_random_uuid(),
    channel_id  text not null default 'allgemein',
    author_id   uuid references auth.users(id) on delete set null,
    author_name text,
    body        text not null,
    created_at  timestamptz not null default now()
);

create index if not exists chat_messages_channel_created_idx
    on public.chat_messages (channel_id, created_at desc);

-- WebRTC-Signaling: Offer/Answer/ICE/Hangup zwischen zwei Nutzern.
-- Der Nachrichteninhalt (SDP/ICE) liegt als Text in "payload".
create table if not exists public.call_signals (
    id           uuid primary key default gen_random_uuid(),
    call_id      uuid not null,
    sender_id    uuid not null,
    recipient_id uuid not null,
    kind         text not null,   -- invite | accept | decline | offer | answer | ice | hangup
    payload      text,
    created_at   timestamptz not null default now()
);

create index if not exists call_signals_recipient_idx
    on public.call_signals (recipient_id, created_at);

alter table public.chat_channels enable row level security;
alter table public.chat_messages enable row level security;
alter table public.call_signals  enable row level security;

-- Kanäle: alle Angemeldeten dürfen lesen
drop policy if exists "chat_channels_select" on public.chat_channels;
create policy "chat_channels_select" on public.chat_channels
    for select using (auth.role() = 'authenticated');

-- Nachrichten: alle Angemeldeten lesen; nur als man selbst und nicht gesperrt schreiben
drop policy if exists "chat_messages_select" on public.chat_messages;
create policy "chat_messages_select" on public.chat_messages
    for select using (auth.role() = 'authenticated');

drop policy if exists "chat_messages_insert" on public.chat_messages;
create policy "chat_messages_insert" on public.chat_messages
    for insert with check (
        auth.uid() = author_id
        and coalesce((select not coalesce(is_banned, false) from public.profiles where id = auth.uid()), true)
    );

-- Anruf-Signale: nur Sender/Empfänger sehen, schreiben oder aufräumen
drop policy if exists "call_signals_select" on public.call_signals;
create policy "call_signals_select" on public.call_signals
    for select using (auth.uid() = sender_id or auth.uid() = recipient_id);

drop policy if exists "call_signals_insert" on public.call_signals;
create policy "call_signals_insert" on public.call_signals
    for insert with check (auth.uid() = sender_id);

drop policy if exists "call_signals_delete" on public.call_signals;
create policy "call_signals_delete" on public.call_signals
    for delete using (auth.uid() = sender_id or auth.uid() = recipient_id);

-- ============================================================
-- Extras: Direktnachrichten-Hilfsfunktion für einheitliche Kanal-IDs
-- ============================================================
create or replace function public.dm_channel(a uuid, b uuid)
returns text language sql immutable as $$
    select 'dm-' || least(a::text, b::text) || '-' || greatest(a::text, b::text);
$$;

-- Heartbeat: setzt last_seen auf jetzt (für alle Angemeldeten)
create or replace function public.heartbeat()
returns void language sql security definer set search_path = public as $$
    update public.profiles set last_seen = now() where id = auth.uid();
$$;

-- ============================================================
-- Kommunikation – Erweiterung: Antworten, Anhänge, Sperre,
-- Freundschaften, Admin-Schalter (Feature-Flags) und Moderation
-- ============================================================

-- Nachrichten: Antworten (Reply), Dateianhänge und Soft-Delete (Moderation)
alter table public.chat_messages add column if not exists reply_to_id uuid;
alter table public.chat_messages add column if not exists reply_preview text;
alter table public.chat_messages add column if not exists attachment_url text;
alter table public.chat_messages add column if not exists attachment_name text;
alter table public.chat_messages add column if not exists attachment_size bigint;
alter table public.chat_messages add column if not exists deleted boolean not null default false;

-- DM-Mitgliedschaft anhand der Kanal-ID "dm-<uidA>-<uidB>" (feste Längen)
create or replace function public.is_dm_member(ch text)
returns boolean
language sql stable
as $$
    select case
        when ch not like 'dm-%' then true
        when length(ch) < 76 then false
        else auth.uid()::text in (substring(ch from 4 for 36), substring(ch from 41 for 36))
    end;
$$;

-- Auswahl: allgemeine Kanäle für alle Angemeldeten, DMs nur für die Beteiligten
drop policy if exists "chat_messages_select" on public.chat_messages;
create policy "chat_messages_select" on public.chat_messages
    for select using (auth.role() = 'authenticated' and public.is_dm_member(channel_id));

-- Schreiben: als man selbst, nicht gesperrt, und nur in Kanälen, in denen man Mitglied ist
drop policy if exists "chat_messages_insert" on public.chat_messages;
create policy "chat_messages_insert" on public.chat_messages
    for insert with check (
        auth.uid() = author_id
        and public.is_dm_member(channel_id)
        and coalesce((select not coalesce(is_banned, false) from public.profiles where id = auth.uid()), true)
    );

-- Moderation: eigener Lösch-Button + Admin darf löschen; zusätzlich "deleted" setzen
drop policy if exists "chat_messages_delete" on public.chat_messages;
create policy "chat_messages_delete" on public.chat_messages
    for delete using (auth.uid() = author_id or public.is_admin());

drop policy if exists "chat_messages_update" on public.chat_messages;
create policy "chat_messages_update" on public.chat_messages
    for update using (auth.uid() = author_id or public.is_admin())
    with check (auth.uid() = author_id or public.is_admin());

-- Sperrliste (Blockieren)
create table if not exists public.blocks (
    blocker_id uuid not null references auth.users(id) on delete cascade,
    blocked_id uuid not null references auth.users(id) on delete cascade,
    created_at timestamptz not null default now(),
    primary key (blocker_id, blocked_id)
);
alter table public.blocks enable row level security;
drop policy if exists "blocks_select_own" on public.blocks;
create policy "blocks_select_own" on public.blocks for select using (auth.uid() = blocker_id);
drop policy if exists "blocks_insert_own" on public.blocks;
create policy "blocks_insert_own" on public.blocks for insert with check (auth.uid() = blocker_id);
drop policy if exists "blocks_delete_own" on public.blocks;
create policy "blocks_delete_own" on public.blocks for delete using (auth.uid() = blocker_id);

-- Freundschaften
create table if not exists public.friendships (
    id           uuid primary key default gen_random_uuid(),
    requester_id uuid not null references auth.users(id) on delete cascade,
    addressee_id uuid not null references auth.users(id) on delete cascade,
    status       text not null default 'pending',   -- pending | accepted
    created_at   timestamptz not null default now(),
    unique (requester_id, addressee_id)
);
alter table public.friendships enable row level security;
drop policy if exists "friendships_select" on public.friendships;
create policy "friendships_select" on public.friendships
    for select using (auth.uid() = requester_id or auth.uid() = addressee_id);
drop policy if exists "friendships_insert" on public.friendships;
create policy "friendships_insert" on public.friendships
    for insert with check (auth.uid() = requester_id);
drop policy if exists "friendships_update" on public.friendships;
create policy "friendships_update" on public.friendships
    for update using (auth.uid() = requester_id or auth.uid() = addressee_id)
    with check (auth.uid() = requester_id or auth.uid() = addressee_id);
drop policy if exists "friendships_delete" on public.friendships;
create policy "friendships_delete" on public.friendships
    for delete using (auth.uid() = requester_id or auth.uid() = addressee_id);

-- Admin-Schalter (Feature-Flags): Funktionen für Nicht-Admins an/aus schalten
create table if not exists public.app_settings (
    key        text primary key,
    value      boolean not null default true,
    updated_at timestamptz not null default now()
);
insert into public.app_settings (key, value) values
    ('chat_enabled', true),
    ('dm_enabled', true),
    ('voice_enabled', true),
    ('video_enabled', true),
    ('files_enabled', true),
    ('websearch_enabled', true),
    ('cleanup_enabled', true),
    ('games_enabled', true),
    ('bugreport_enabled', true),
    ('downloads_enabled', true),
    ('internet_checker_enabled', true)
on conflict (key) do nothing;

alter table public.app_settings enable row level security;
drop policy if exists "app_settings_select" on public.app_settings;
create policy "app_settings_select" on public.app_settings
    for select using (auth.role() = 'authenticated' or auth.role() = 'anon');
drop policy if exists "app_settings_admin" on public.app_settings;
create policy "app_settings_admin" on public.app_settings
    for all using (public.is_admin()) with check (public.is_admin());

-- Nutzer per E-Mail finden (für "DM per E-Mail" und Freundschaftsanfragen)
create or replace function public.find_user_by_email(p_email text)
returns table (id uuid, display_name text, email text, avatar_url text, last_seen timestamptz)
language sql security definer set search_path = public stable as $$
    select id, display_name, email, avatar_url, last_seen
    from public.profiles
    where lower(email) = lower(trim(p_email))
    limit 1;
$$;

-- ============================================================
-- Dateianhänge: Storage-Bucket (öffentlich lesbar, angemeldet schreibbar)
-- ============================================================
insert into storage.buckets (id, name, public)
values ('chat-attachments', 'chat-attachments', true)
on conflict (id) do update set public = true;

drop policy if exists "chat_attachments_read" on storage.objects;
create policy "chat_attachments_read" on storage.objects
    for select using (bucket_id = 'chat-attachments');

drop policy if exists "chat_attachments_insert" on storage.objects;
create policy "chat_attachments_insert" on storage.objects
    for insert with check (bucket_id = 'chat-attachments' and auth.role() = 'authenticated');

-- ============================================================
-- Premium: Codes, Aktivität, Sperre und 3-Monats-Abzeichen
-- Nicht-monetär: Codes (aus der Admin-UI) oder über das Erreichen
-- von Abzeichen (Aktivität). Beides schaltbar per Feature-Flag.
-- Premium gewährt u.a. 4 Wochen statt 1 Woche Ressourcen-Verlauf.
-- Nach Ablauf (premium_until in der Vergangenheit) behandelt die
-- App den Nutzer automatisch wieder wie ein Nicht-Premium-Konto.
-- ============================================================

alter table public.profiles add column if not exists premium_until  timestamptz;
alter table public.profiles add column if not exists premium_since  timestamptz;
alter table public.profiles add column if not exists premium_banned boolean not null default false;

create table if not exists public.premium_codes (
    id         uuid primary key default gen_random_uuid(),
    code       text unique not null,
    -- Laufzeit in Tagen (null = unendlich). Alternative: valid_until.
    duration_days int,
    -- Gültig-bis-Datum (null = unendlich). Wenn beide gesetzt, gilt das spätere.
    valid_until timestamptz,
    created_by uuid references auth.users(id) on delete set null,
    created_at timestamptz not null default now()
);

-- Einlösungen: jeder Nutzer kann JEDEN Code höchstens einmal einlösen.
-- Ein Code ist damit nicht global "verbraucht", sondern pro Nutzer nutzbar.
create table if not exists public.premium_code_uses (
    code_id  uuid not null references public.premium_codes(id) on delete cascade,
    user_id  uuid not null references auth.users(id) on delete cascade,
    used_at  timestamptz not null default now(),
    primary key (code_id, user_id)
);

create table if not exists public.premium_claims (
    user_id    uuid not null references auth.users(id) on delete cascade,
    milestone  text not null,
    days_added int not null default 7,
    claimed_at timestamptz not null default now(),
    primary key (user_id, milestone)
);

-- Feature-Flags: Premium-Quellen an/aus
insert into public.app_settings (key, value) values
    ('premium_codes_enabled', true),
    ('premium_activity_enabled', true)
on conflict (key) do nothing;

alter table public.premium_codes     enable row level security;
alter table public.premium_code_uses enable row level security;
alter table public.premium_claims    enable row level security;

-- Codes / Claims sind nur für Admins einsehbar. Nutzer verwenden die
-- RPC-Funktionen (security definer) zum Einlösen – niemals direkt.
drop policy if exists "premium_codes_admin" on public.premium_codes;
create policy "premium_codes_admin" on public.premium_codes
    for all using (public.is_admin()) with check (public.is_admin());

drop policy if exists "premium_code_uses_admin" on public.premium_code_uses;
create policy "premium_code_uses_admin" on public.premium_code_uses
    for all using (public.is_admin()) with check (public.is_admin());

drop policy if exists "premium_claims_select_own" on public.premium_claims;
create policy "premium_claims_select_own" on public.premium_claims
    for select using (auth.uid() = user_id);

-- Hilfsfunktion: setzt premium_since beim ersten Premium und vergibt das
-- "3 Monate in Folge Premium"-Abzeichen, sobald die Bedingung erfüllt ist.
create or replace function public.ensure_premium_and_badge(v_uid uuid)
returns void
language plpgsql
security definer set search_path = public
as $$
begin
    update public.profiles
       set premium_since = coalesce(premium_since, now())
     where id = v_uid;

    if exists (select 1 from public.profiles
                where id = v_uid
                  and premium_since <= now() - interval '90 days'
                  and premium_until > now()) then
        insert into public.badges (user_id, badge_id)
        values (v_uid, 'premium_streak3')
        on conflict (user_id, badge_id) do nothing;
    end if;
end;
$$;

-- Gesperrte Nutzer (global oder Premium-Sperre) dürfen kein Premium erhalten.
create or replace function public.premium_allowed(v_uid uuid)
returns boolean
language sql stable security definer set search_path = public
as $$
    select v_uid is not null
       and coalesce((select not coalesce(is_banned, false) and not coalesce(premium_banned, false)
                     from public.profiles where id = v_uid), false);
$$;

-- Code einlösen: pro Nutzer und Code nur einmal. Verlängert premium_until
-- ab jetzt (bzw. ab aktuellem Ablauf, falls schon aktiv).
-- duration_days und/oder valid_until bestimmen die Verlängerung.
-- Wenn beide null → unendlich (premium_until = 'infinity').
create or replace function public.redeem_premium_code(p_code text)
returns jsonb
language plpgsql
security definer set search_path = public
as $$
declare
    v_uid uuid := auth.uid();
    v_enabled boolean;
    v_code_id uuid;
    v_duration int;
    v_valid_until timestamptz;
    v_new_until timestamptz;
begin
    if v_uid is null then
        return jsonb_build_object('ok', false, 'error', 'login_required');
    end if;
    if not public.premium_allowed(v_uid) then
        return jsonb_build_object('ok', false, 'error', 'premium_banned');
    end if;

    select coalesce((select value from public.app_settings where key = 'premium_codes_enabled'), true)
      into v_enabled;
    if not v_enabled then
        return jsonb_build_object('ok', false, 'error', 'codes_disabled');
    end if;

    select id, duration_days, valid_until into v_code_id, v_duration, v_valid_until
      from public.premium_codes
     where code = upper(trim(p_code))
     limit 1;

    if v_code_id is null then
        return jsonb_build_object('ok', false, 'error', 'invalid_code');
    end if;

    begin
        insert into public.premium_code_uses (code_id, user_id)
        values (v_code_id, v_uid);
    exception when unique_violation then
        return jsonb_build_object('ok', false, 'error', 'already_used');
    end;

    -- Berechne neues premium_until:
    -- Basis ist max(now(), aktuelles premium_until).
    -- Wenn duration_days gesetzt: addiere Tage zur Basis.
    -- Wenn valid_until gesetzt: nimm max(Basis + duration, valid_until).
    -- Wenn beide null: 'infinity'.
    select greatest(coalesce(premium_until, now()), now()) into v_new_until
      from public.profiles where id = v_uid;

    if v_duration is not null then
        v_new_until := v_new_until + make_interval(days => v_duration);
    end if;
    if v_valid_until is not null then
        v_new_until := greatest(v_new_until, v_valid_until);
    end if;
    if v_duration is null and v_valid_until is null then
        v_new_until := 'infinity'::timestamptz;
    end if;

    update public.profiles
       set premium_until = v_new_until
     where id = v_uid;

    perform public.ensure_premium_and_badge(v_uid);

    return jsonb_build_object('ok', true, 'premium_until', v_new_until);
end;
$$;

-- Aktivitäts-Premium: einmal pro Abzeichen-Meilenstein verlängern.
create or replace function public.claim_activity_premium(p_milestone text)
returns jsonb
language plpgsql
security definer set search_path = public
as $$
declare
    v_uid uuid := auth.uid();
    v_enabled boolean;
    v_days int := 3;
begin
    if v_uid is null then
        return jsonb_build_object('ok', false, 'error', 'login_required');
    end if;
    if not public.premium_allowed(v_uid) then
        return jsonb_build_object('ok', false, 'error', 'premium_banned');
    end if;

    select coalesce((select value from public.app_settings where key = 'premium_activity_enabled'), false)
      into v_enabled;
    if not v_enabled then
        return jsonb_build_object('ok', false, 'error', 'activity_disabled');
    end if;

    case p_milestone
        when 'bibliothekar' then v_days := 7;
        when 'systemprofi'  then v_days := 7;
        when 'aufraeumer'   then v_days := 7;
        when 'bugreporter'  then v_days := 7;
        else v_days := 3;
    end case;

    insert into public.premium_claims (user_id, milestone, days_added)
    values (v_uid, p_milestone, v_days)
    on conflict (user_id, milestone) do nothing
    returning days_added into v_days;

    if v_days is null then
        return jsonb_build_object('ok', false, 'error', 'already_claimed');
    end if;

    update public.profiles
       set premium_until = greatest(coalesce(premium_until, now()),
                                    now() + make_interval(days => v_days))
     where id = v_uid;

    perform public.ensure_premium_and_badge(v_uid);

    return jsonb_build_object('ok', true, 'bonus_days', v_days);
end;
$$;

grant execute on function public.redeem_premium_code(text)       to authenticated;
grant execute on function public.claim_activity_premium(text)    to authenticated;

-- Admin-Ansicht: Codes inkl. Einlösungs-Zähler (für Admin-UI im Web).
create or replace function public.fetch_premium_codes()
returns jsonb
language plpgsql
security definer set search_path = public
as $$
declare
    v_uid uuid := auth.uid();
    v_result jsonb;
begin
    if v_uid is null or not public.is_admin() then
        return jsonb_build_array();
    end if;
    select coalesce(jsonb_agg(jsonb_build_object(
        'id', c.id,
        'code', c.code,
        'duration_days', c.duration_days,
        'valid_until', c.valid_until,
        'created_at', c.created_at,
        'uses', (select count(*) from public.premium_code_uses u where u.code_id = c.id),
        'last_used_at', (select max(u.used_at) from public.premium_code_uses u where u.code_id = c.id)
    ) order by c.created_at desc), jsonb_build_array())
      into v_result from public.premium_codes c;
    return v_result;
end;
$$;

grant execute on function public.fetch_premium_codes() to authenticated;
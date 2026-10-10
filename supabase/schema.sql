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
    status         text not null default 'open',   -- open | in_progress | done | rejected
    admin_reply    text,
    created_at     timestamptz not null default now(),
    updated_at     timestamptz not null default now()
);

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

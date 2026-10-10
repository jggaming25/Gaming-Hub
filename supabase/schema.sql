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
    created_at   timestamptz not null default now()
);

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

-- badges: für alle lesbar, eigene Abzeichen eintragen
drop policy if exists "badges_select" on public.badges;
create policy "badges_select" on public.badges
    for select using (true);

drop policy if exists "badges_insert_own" on public.badges;
create policy "badges_insert_own" on public.badges
    for insert with check (auth.uid() = user_id);

-- bugs: jeder (auch anonym) darf melden; eigene + (Admin: alle) lesen
drop policy if exists "bugs_insert" on public.bugs;
create policy "bugs_insert" on public.bugs
    for insert with check (true);

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

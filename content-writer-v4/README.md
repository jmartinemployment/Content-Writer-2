# Content Writer v4

Jasper-style content generation on the Geek platform.

```
Next.js (this app)
  → GeekAPI            /api/content-writer/v4/*
    → GeekRepository   /repo/content-writer-v4/*
      → Supabase       schema content_writer_v4
```

## Local setup

1. Run GeekRepository (`:5050`) and GeekAPI (`:8080`) from `GeekBackend` with `DATABASE_URL` pointing at Supabase (or local Postgres). The `content_writer_v4` schema is migrated and seeded on GeekRepository startup.
2. Copy `.env.example` to `.env.local` and set `NEXT_PUBLIC_API_URL`.
3. `npm install && npm run dev`

Auth is deferred (Phase 5 / GeekOAuth). The app sends `Authorization: Bearer <dev-user-guid>` for now.

## Routes

- `/templates` — catalog gallery
- `/templates/[slug]` — dynamic form, provider dropdown, generate, save
- `/documents` — saved documents
- `/documents/[id]` — edit / regenerate / delete
- `/brand-voices` — CRUD tone profiles
- `/usage` — token and cost dashboard

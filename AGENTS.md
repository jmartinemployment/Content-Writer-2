<!-- BEGIN:nextjs-agent-rules -->
# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` before writing any code. Heed deprecation notices.
<!-- END:nextjs-agent-rules -->

# No database

This backend (`backend/`, .NET) has no database, no EF Core, no repository pattern. Project/Client state lives in a plain in-process object store (`ProjectStore`/`ClientStore` in `ContentWriter.Infrastructure/InMemory/`), for the process lifetime only — it resets on every restart/redeploy, by design.

This isn't a stopgap or a stylistic preference: two earlier database-backed designs for storing/serving content were tried and vetoed because they couldn't support the fine-grained per-element access this pipeline needs — direct addressing like `entry.sections[1].heading` (see `geekatyourspot/src/components/content-writer/content-page.tsx`), moving to a flat `entry.blocks[n]` array. Relational/nested DB storage fought that access pattern; a flat, file-based array serialized into `.html` frontmatter doesn't.

The only durable output this app produces is `.html` committed directly to the geekatyourspot GitHub repo via `GeekatyourspotCommitService` (Git Data API) — that commit *is* the save. (Migrated from `.mdx` on 2026-07-21 — the body was always raw HTML off `row.BodyHtml`; `.mdx` was a misnomer, no MDX/JSX compilation ever happened. See `MdxExportService.ToMdxDocument`.) If you're troubleshooting content "not being saved," check whether it was exported/committed, not whether a database has it — there is no database to check.

Do not reintroduce EF Core, a DbContext, migrations, or an `IRepository<T>`-style abstraction. If in-process state needs to grow, extend `ProjectStore`/`ClientStore` directly.

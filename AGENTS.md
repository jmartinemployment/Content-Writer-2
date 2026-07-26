<!-- BEGIN:nextjs-agent-rules -->
# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` before writing any code. Heed deprecation notices.
<!-- END:nextjs-agent-rules -->

# No database

This backend (`backend/`, .NET) has no database, no EF Core, no repository pattern. Project/Client state lives in a plain in-process object store (`ProjectStore`/`ClientStore` in `ContentWriter.Infrastructure/InMemory/`), for the process lifetime only — it resets on every restart/redeploy, by design.

This isn't a stopgap or a stylistic preference: two earlier database-backed designs for storing/serving content were tried and vetoed because they couldn't support the fine-grained per-element access this pipeline needs — direct addressing like `document.sections[1].children[0].paragraphs`. Relational/nested DB storage fought that access pattern; a flat, file-based structure doesn't.

The only durable output this app produces is `.html` committed directly to the geekatyourspot GitHub repo via `GeekatyourspotCommitService` (Git Data API) — that commit *is* the save. If you're troubleshooting content "not being saved," check whether it was exported/committed, not whether a database has it — there is no database to check.

**Content shape (rewritten 2026-07-22):** the pipeline no longer touches Markdown or YAML anywhere. Content is authored by the LLM as structured `Section`/`Paragraph`/`Run` records (`ContentDocument` — see `ContentWriter.Domain.Entities.ContentDocument`), never as a markup or Markdown string — headings are plain-text fields, bold/italic/links are boolean/url fields on a `Run`, never `**`/`##`/`[text](url)` syntax. Export (`HtmlExportService`/`SectionHtmlRenderer`) builds a real DOM via HtmlAgilityPack and serializes it to a standalone `<!doctype html>` file — no YAML frontmatter, no Markdig, no re-parsing text to recover structure. `MdxExportService`, `MdxDocument`, `ArticleHtmlSectionExtractor`, `ToolsSectionHtmlParser`, and `HtmlBodyNormalizer` are gone; do not reintroduce Markdown/Markdig anywhere in this pipeline.

Do not reintroduce EF Core, a DbContext, migrations, or an `IRepository<T>`-style abstraction. If in-process state needs to grow, extend `ProjectStore`/`ClientStore` directly.

# Lede Section

The lede is a `Section` (the first element of `ContentDocument.Lede`) — not a separate content type. It must have:
- **Tag**: `"h2"` (always)
- **Heading**: prose text based on lede type:
  - **Summary**: a condensed summary of the article's overall topic
  - **Surprise**: a hook or surprising fact that mirrors/complements the main topic
  - **Question**: an opening question that sets up or frames the article's topic
- **ImagePrompt** (optional): text that describes what image should accompany the lede

Treat the lede as a regular Section throughout the pipeline — same structure, same export rules, no special handling.

# Editorial rewrite

User-triggered "Rewrite with feedback" (`ReviewLoopService.RewriteFromLatestVerdictAsync`) passes extracted reviewer notes into pillar generation as follows:

- **Body H2s / FAQ** — already scoped via `BuildRevisionNotesBlock` (`[Section: "…"]` self-filter).
- **Lede** — `BuildArticleLedePrompt` receives notes tagged with the current lede heading, or section titles not in the body outline (lede-only). Meta/title and FAQ notes are excluded from the lede call.
- **Meta description / title** — when notes target `[Section: "Meta description"]` or `Title`, `BuildArticleMetaRevisionPrompt` runs a small revise pass and updates the article row before body regen. Outline stays stable unless notes explicitly demand outline changes (PAA "remove section" should reframe FAQ guidance, not delete the FAQ H2).

The editorial reviewer (`EditorialReviewService`) must not demand unverifiable inventions (exact fines, named real case studies, unsupported percentages). Prefer pain-first framing, labeled hypotheticals for specificity, and remove/replace invented-sounding absolutes — aligned with writer prompts.

# Pillar Tools section generation

Pillar body generation calls the LLM **once per top-level H2**, including Tools. The Tools H2 is assembled from:

1. A lightweight platform-list call (4–5 real product names).
2. One LLM call per platform returning that platform’s `h3` subtree (overview, capability list, implementer `h4`).
3. Assembly under the Tools H2 — `ToolSectionExtractor` still finds `h3` platforms the same way.

Do not collapse Tools back into a single mega-JSON call with a raised `MaxOutputTokens` ceiling; per-platform generation is the intended design.

# Queued work (next session)

See [plans/tomorrow-rewrite-and-tools.md](plans/tomorrow-rewrite-and-tools.md) — Priority 1 (editorial rewrite fidelity) and Priority 2 (Tools per-platform) are implemented. Remaining: end-to-end re-test after a single deploy, then pause deploys so in-memory projects aren’t wiped mid-run.


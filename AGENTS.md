<!-- BEGIN:nextjs-agent-rules -->
# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` before writing any code. Heed deprecation notices.
<!-- END:nextjs-agent-rules -->

# Persistence and target architecture

`backend/` persists `Project`/`Client` state via a write-through cache + swappable `IPersistenceStore`
(`ContentWriter.Infrastructure/{IPersistenceStore,FileSystemPersistenceStore,PersistentProjectStore,PersistentClientStore}.cs`).
Currently backed by the local filesystem (`FileSystemPersistenceStore`) — durable across process
restarts, but **ephemeral on Railway** (wiped on every redeploy, no volume attached). A
`GeekRepositoryPersistenceStore` calling GeekRepository directly was built and then **deliberately
not activated** — GeekRepository accepts calls only from GeekAPI (see
`GeekBackend/AGENTS.md` § "Service topology & trust boundaries"), and this service calling it
directly would have violated that boundary.

**Target architecture, not yet built:** this service is slated to merge into GeekAPI — its
Controllers/Application/Domain/Infrastructure moving into GeekAPI's process, retiring the
standalone Railway deployment. Once merged, persistence becomes a direct in-process call to
GeekRepository using the credential GeekAPI already legitimately holds — no new secret, no HTTP
hop. Until that merge happens, **do not add a new direct-to-GeekRepository credential here** — if
GeekRepository-backed persistence is needed in the interim, it goes through a new GeekAPI proxy
route (see `GeekAPI/CLAUDE.md`), not a copy of `REPO_API_KEY`.

Two earlier database-backed designs for storing/serving *content* (not app state — see below) were
tried and vetoed because they couldn't support the fine-grained per-element access this pipeline
needs — direct addressing like `document.sections[1].children[0].paragraphs`. Relational/nested DB
storage fought that access pattern. The current `IPersistenceStore` design avoids repeating that
mistake: it stores each `Project`/`Client` as one JSON blob (via `ProjectSnapshotSerializer`/
`ClientSnapshotSerializer`), not shredded into relational tables, so the object graph — and that
fine-grained access — stays intact regardless of which backend implements the interface.

The only *durably published* output this app produces is `.html` committed directly to the
geekatyourspot GitHub repo via `GeekatyourspotCommitService` (Git Data API) — that commit *is* the
publish step, separate from the app-state persistence described above.

**Content shape:** the pipeline never touches Markdown or YAML anywhere. Content is authored by the
LLM as structured `Section`/`Paragraph`/`Run` records (`ContentDocument` — see
`ContentWriter.Domain.Entities.ContentDocument`), never as a markup or Markdown string — headings
are plain-text fields, bold/italic/links are boolean/url fields on a `Run`, never `**`/`##`/`[text](url)`
syntax. Export (`HtmlExportService`/`SectionHtmlRenderer`) builds a real DOM via HtmlAgilityPack and
serializes it to a standalone `<!doctype html>` file — no YAML frontmatter, no Markdig, no
re-parsing text to recover structure. Do not reintroduce Markdown/Markdig anywhere in this pipeline.

Do not reintroduce EF Core, a DbContext, migrations, or an `IRepository<T>`-style abstraction here
directly — persistence is the `IPersistenceStore` seam described above, and the real database work
(if any) belongs in GeekRepository, reached through GeekAPI.

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
2. One LLM call per platform returning that platform's `h3` subtree (overview, capability list, implementer `h4`).
3. Assembly under the Tools H2 — `ToolSectionExtractor` still finds `h3` platforms the same way.

Do not collapse Tools back into a single mega-JSON call with a raised `MaxOutputTokens` ceiling; per-platform generation is the intended design.

# Required-heading and gap-check features (added 2026-07-29)

Project.Notes (comma-separated topics, set via the frontend "Notes" panel) are threaded into
`ProjectGenerationContext.DesiredHeadings` and consumed **only** by the pillar Introduction
section's body-generation prompt — each topic is required as an `h3` (with `h4` depth) inside
whatever section classifies as the Introduction/Overview, not as a forced top-level H2. An earlier
version forced these into their own top-level H2s via `PillarOutlineNormalizer`; that mechanism was
removed as unnatural placement disconnected from the article's actual content — do not reintroduce it.

`GeneratedContent.Gaps` and `GeneratedContent.NoResearchWarning` are soft, code-computed advisories
(not hard blocks) surfaced via `GeneratedContentSet.ArticleGaps`/`ArticleNoResearchWarning` and
rendered in `ContentResults.tsx` — a required topic that didn't end up as a heading anywhere, or a
generation that ran from nothing but the bare keyword, is reported to the operator rather than
silently accepted or silently logged where no one will see it.

Home-page Use Case grounding: `CrawledSite.UseCases` (extracted via one LLM call over the crawled
Home page's own content, `CrawlController.TryExtractUseCasesWithLlmAsync`) is matched against
`project.TargetKeyword` in `ContentGenerationOrchestrator.BuildContext` — a match grounds the
outline and Introduction section in the real description already published on the client's Home
page, so the generated pillar is demonstrably the page that Home-page item was pointing at, not a
generic treatment of the keyword. No-op when there's no crawl or no match.

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

# Pillar Tools section generation (known limitation)

Pillar body generation already calls the LLM **once per top-level H2**. The Tools H2 is an exception in workload, not in call count: that single call must return one nested JSON `Section` containing 4–5 platform `h3` children (each with paragraphs, a list, and an `h4` implementer subtree). Structured JSON overhead means even a ~700–900 word Tools section can exceed a modest `MaxOutputTokens` budget and truncate mid-JSON (invalid parse → Step 2 failure). The current mitigation is a higher token ceiling (`8192` for Tools vs `2048` for other pillar sections) plus tighter platform-count guidance — that is a ceiling fix, not a design fix.

**Preferred long-term approach:** generate one platform (or one child subtree) per LLM call and assemble them under the Tools H2 — same fine-grained pattern as the rest of the pillar — so a high per-call token budget is no longer required for that step. Do not treat raising `MaxOutputTokens` further as the primary solution if this shape keeps growing.

# Queued work (next session)

See [plans/tomorrow-rewrite-and-tools.md](plans/tomorrow-rewrite-and-tools.md):

1. **Editorial rewrite fidelity** — pass review notes into lede (+ meta when relevant); align reviewer rubric with no-invented-facts rules so rewrite → re-review is not a stuck loop.
2. **Tools per-platform generation** — implement the preferred approach above (this section).


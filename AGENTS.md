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

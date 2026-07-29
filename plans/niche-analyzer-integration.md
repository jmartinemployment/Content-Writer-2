# Someday: reuse the Niche/Site Analyzer's gap and orphan-pillar detection

Deferred pickup plan — not scheduled, no commitment to build. Written 2026-07-29 during the
GeekAPI merge work, to capture the finding rather than lose it or re-derive it later.

## What this is

`Geek-SEO/GeekSeoBackend/Services/NicheExtraction/PillarActionRecommender.cs` (and the broader
Niche Analyzer pipeline it sits inside — `NicheAnalyzerService.cs`, `TopicalMapService.cs`, ~7,000
lines across 34 files in `NicheExtraction/`) already computes exactly the kind of thing that would
make content-writer-v2's output better:

- **`link_orphan_pillar`** — a selected pillar with no internal links to/from other pillars. This
  is precisely the "topics I already have written, but not linked to anything" observation from
  testing today.
- **`suggest_pillar_page`** — a high-confidence topic with no dedicated URL yet.
- **`entity_thin_content`** — SERP expects more related entities than the site's content covers.

Also already ported and live: `TopicClusteringService.ClusterKeywordList` (keyword dedup) — copied
into content-writer-v2 on 2026-07-29. That's the easy, self-contained piece. This doc is about the
rest, which is not self-contained.

## Why it's not a quick add

The Niche Analyzer is a full async pipeline, not a callable utility:
- `POST /api/seo/niche-analyzer/analyze` enqueues a multi-step background job for an entire domain;
  results (`/gaps`, `/coverage-matrix`, `/entities`) are only available once that job completes.
- It runs its own user-auth model (`ICurrentUserContext.RequireUserId()`), separate from how
  content-writer-v2 (or GeekAPI, post-merge) identifies a caller.
- It analyzes a whole site's topic coverage (schema.org, sitemap, nav menus, internal link graph,
  SERP entities, GSC queries, competitor pages) — a much bigger analysis surface than
  content-writer-v2 has any concept of today (it currently only has: one crawl pass + whatever
  keyword files get uploaded + the Home-page Use Case extraction added 2026-07-29).

The smaller, native alternative already shipped instead: `GeneratedContent.Gaps` /
`NoResearchWarning` (added 2026-07-29) — checks whether Notes topics and the matched Use Case name
actually landed as headings in the *current* generated document. That's real but narrow: it can't
tell you "this pillar has no inbound links from your other pillars" the way `link_orphan_pillar`
can, because it has no visibility into the rest of the site's link graph.

## If this gets picked up

Two shapes worth considering, not a decision made yet:

1. **Call the real Niche Analyzer**, post-merge (once content-writer-v2 lives inside GeekAPI):
   trigger `analyze`, poll `/status`, read `/gaps` once complete, surface `link_orphan_pillar` /
   `suggest_pillar_page` actions in the UI. Real, powerful, but inherits the whole async-job/polling
   UX and the "analyze a whole domain" scope — probably too heavy to run per-generation.
2. **Extract just the internal-link-graph piece** (`InternalLinkGraphBuilder.cs`,
   `PillarActionRecommender.cs`'s orphan-detection logic specifically) as a second native port,
   same treatment as `TopicClusteringService` — feed it content-writer-v2's own already-crawled
   site data instead of the Niche Analyzer's fuller pipeline. Smaller, but reimplements a slice of
   what the real system already does well.

No recommendation between these yet — revisit once there's an actual reason to prioritize this
over other work, not on a schedule.

# Deferred: generation requests can outlive the HTTP request that started them

**Status (2026-07-30): deferred, not scheduled.** Confirmed pre-existing, not a regression from
today's pillar cost-reduction work — recorded here so it's tracked rather than re-discovered.

## The problem

A full pillar body generation (`POST /api/projects/{id}/generate/pillar/body`) took ~3 minutes
end-to-end in a live test today. That exceeds:
- Railway's edge/proxy timeout (~90-100s) — a direct call without a client-side `--max-time`
  override got a 502 after ~97s even though the server kept working and the generation later
  completed and saved successfully.
- Very likely Vercel's default serverless function timeout for the frontend's proxy route
  (`frontend/src/app/api/cw/[...path]/route.ts` has no `export const maxDuration` set, so it uses
  Vercel's plan-tier default) — never directly confirmed, but the shape of the problem is the same:
  a synchronous HTTP request held open for the full duration of a multi-minute generation.

**Net effect**: a real user clicking "Generate" in the actual UI likely sees a browser-level
timeout/error, even though the generation itself is correct and the project gets updated
successfully behind the scenes. The user has to know to reload/re-check rather than trust the
error message.

## Why this isn't new today

`ContentGenerationOrchestrator`'s generation endpoints have always been synchronous, single-request
operations — a pillar body was already several sequential LLM calls (lede, N section calls, Tools
platform calls, FAQ) before today's consolidation work. Today's changes (fewer, larger calls) may
have shifted the specific timing profile, but the fundamental issue — a long-running generation
tied 1:1 to the lifetime of the HTTP request that triggered it — predates this session.

## What a real fix looks like (not scoped/decided yet)

Options, not evaluated in depth:
- Raise `maxDuration` on the Vercel proxy route and check/raise Railway's edge timeout — a
  band-aid that helps until generation gets slow enough to blow through the new ceiling too.
- Make generation async: kick off the job, return immediately with a job id, let the frontend poll
  for status — the correct fix, but real scope (new job/status model, UI polling, error handling for
  a job that fails after the triggering request already returned).

## Where this connects to existing prior art

`plans/content-writer-v2.plan.md` (superseded) already describes a `BatchJob`/`BatchWorker`
architecture that was built for a different reason (batch/parallel generation) and then descoped —
worth checking whether that existing (unused) infrastructure could be repurposed for async
single-project generation instead of building a new job model from scratch, if/when this gets
picked up.

"use client";

import { useState } from "react";
import { crawlProject, updateProjectTone, ApiError } from "@/lib/content-writer/api";
import { BRAND_TONES } from "@/lib/content-writer/brand-tones";
import type { CrawlSummary } from "@/lib/content-writer/types";

export default function CrawlPanel({
  projectId,
  projectUrl,
  crawl,
  onCrawled,
}: {
  projectId: string;
  projectUrl: string;
  crawl: CrawlSummary | null;
  onCrawled: (summary: CrawlSummary) => void;
}) {
  const [isCrawling, setIsCrawling] = useState(false);
  const [isSavingTone, setIsSavingTone] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleCrawl() {
    setError(null);
    setIsCrawling(true);
    try {
      const summary = await crawlProject(projectId);
      onCrawled(summary);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Crawl failed. Check the project URL and try again.");
    } finally {
      setIsCrawling(false);
    }
  }

  async function handleToneChange(toneId: string) {
    if (!crawl || toneId === crawl.detectedTone) {
      return;
    }

    setError(null);
    setIsSavingTone(true);
    try {
      const summary = await updateProjectTone(projectId, toneId);
      onCrawled(summary);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not update tone.");
    } finally {
      setIsSavingTone(false);
    }
  }

  const toneValue =
    crawl && BRAND_TONES.some((t) => t.id === crawl.detectedTone)
      ? crawl.detectedTone
      : BRAND_TONES[0].id;

  return (
    <div className="rounded-xl border border-border bg-surface p-6 shadow-sm">
      <h2 className="text-lg font-semibold text-foreground">2. Crawl Project Site</h2>
      <p className="mt-1 text-sm text-muted">
        Crawls <span className="font-mono text-foreground">{projectUrl}</span> (sitemap + internal links, up to
        50 pages) for JSON+LD, headings, and paragraph copy to detect brand tone and topical focus.
      </p>

      <button
        onClick={handleCrawl}
        disabled={isCrawling}
        className="mt-4 rounded-md bg-brand px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-brand-dark disabled:opacity-60"
      >
        {isCrawling ? "Crawling..." : crawl ? "Re-crawl Site" : "Crawl Site"}
      </button>

      {error && <p className="mt-4 text-sm text-red-600">{error}</p>}

      {crawl && (
        <dl className="mt-5 grid grid-cols-2 gap-4 rounded-lg bg-background p-4 text-sm sm:grid-cols-3">
          <div>
            <dt className="text-muted">Site Name</dt>
            <dd className="font-medium text-foreground">{crawl.siteName}</dd>
          </div>
          <div>
            <dt className="text-muted">Pages Crawled</dt>
            <dd className="font-medium text-foreground">{crawl.pagesCrawled}</dd>
          </div>
          <div>
            <dt className="text-muted">JSON+LD Blocks</dt>
            <dd className="font-medium text-foreground">{crawl.jsonLdBlockCount}</dd>
          </div>
          <div className="col-span-2 sm:col-span-3">
            <dt className="text-muted">
              <label htmlFor="brand-tone">Tone</label>
            </dt>
            <dd className="mt-1">
              <select
                id="brand-tone"
                value={toneValue}
                disabled={isSavingTone}
                onChange={(e) => void handleToneChange(e.target.value)}
                className="w-full max-w-xl rounded-md border border-border bg-surface px-3 py-2 text-sm font-medium text-foreground disabled:opacity-60"
              >
                {BRAND_TONES.map((tone) => (
                  <option key={tone.id} value={tone.id}>
                    {tone.label}
                  </option>
                ))}
              </select>
              <p className="mt-1 text-xs text-muted">Brand voice for generated content. Re-crawl resets to a mapped default.</p>
            </dd>
          </div>
          <div className="col-span-2 sm:col-span-3">
            <dt className="text-muted">Detected Focus</dt>
            <dd className="font-medium text-foreground">{crawl.detectedFocus}</dd>
          </div>
        </dl>
      )}
    </div>
  );
}

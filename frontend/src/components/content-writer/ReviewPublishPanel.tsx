"use client";

import { useState } from "react";
import {
  commitHtmlExportToGitHub,
  downloadHtmlExport,
  runReview,
  ApiError,
} from "@/lib/content-writer/api";
import type {
  CommitHtmlExportResult,
  GeneratedContentSet,
  ReviewVerdict,
} from "@/lib/content-writer/types";

export default function ReviewPublishPanel({
  projectId,
  result,
}: {
  projectId: string;
  result: GeneratedContentSet | null;
}) {
  const [verdicts, setVerdicts] = useState<ReviewVerdict[] | null>(null);
  const [isReviewing, setIsReviewing] = useState(false);
  const [reviewError, setReviewError] = useState<string | null>(null);
  const [reviewTypes, setReviewTypes] = useState({ pillar: true, blog: true, tools: true });
  const [testToolSlug, setTestToolSlug] = useState<string | null>(null);

  const [isExporting, setIsExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  const [includeRevise, setIncludeRevise] = useState(true);

  const [isCommitting, setIsCommitting] = useState(false);
  const [commitError, setCommitError] = useState<string | null>(null);
  const [commitResult, setCommitResult] = useState<CommitHtmlExportResult | null>(null);

  const hasPublishableContent = (result?.article?.wordCount ?? 0) > 0 || result?.blog != null;

  async function handleReview() {
    setReviewError(null);
    setIsReviewing(true);
    try {
      const contentTypes = [
        reviewTypes.pillar && "TechnicalArticle",
        reviewTypes.blog && "BlogPost",
        reviewTypes.tools && "ToolPost",
      ].filter((t): t is string => Boolean(t));
      const toolSlug = reviewTypes.tools && result?.toolPosts?.[0]?.slug ? result.toolPosts[0].slug : null;
      const next = await runReview(projectId, contentTypes, toolSlug);
      setVerdicts(next);
      setTestToolSlug(toolSlug);
    } catch (err) {
      setReviewError(err instanceof ApiError ? err.message : "Review failed.");
    } finally {
      setIsReviewing(false);
    }
  }

  async function handleExport() {
    setExportError(null);
    setIsExporting(true);
    try {
      await downloadHtmlExport(projectId, includeRevise);
    } catch (err) {
      setExportError(err instanceof ApiError ? err.message : "Export failed.");
    } finally {
      setIsExporting(false);
    }
  }

  async function handleCommit() {
    setCommitError(null);
    setCommitResult(null);
    setIsCommitting(true);
    try {
      const next = await commitHtmlExportToGitHub(projectId, includeRevise);
      setCommitResult(next);
    } catch (err) {
      setCommitError(err instanceof ApiError ? err.message : "Commit failed.");
    } finally {
      setIsCommitting(false);
    }
  }

  const approvedCount = verdicts?.filter((v) => v.status === "Approved").length ?? 0;
  const exhaustedCount = verdicts?.filter((v) => v.status === "Exhausted").length ?? 0;

  if (!hasPublishableContent) {
    return null;
  }

  return (
    <div className="rounded-xl border border-border bg-surface p-6 shadow-sm">
      <h2 className="text-lg font-semibold text-foreground">8. Editorial Review</h2>
      <p className="mt-1 text-sm text-muted">
        A different model reviews each selected row (invented-feature/fact check, brand-voice consistency) —
        single pass, no revise-and-retry. Optional: a never-reviewed row can still be exported; only a row
        reviewed and NOT Approved is excluded. Reviewing fewer rows at once also avoids provider rate limits.
      </p>

      <div className="mt-3 flex flex-wrap gap-4 text-sm text-foreground">
        <label className="flex items-center gap-1.5">
          <input
            type="checkbox"
            checked={reviewTypes.pillar}
            onChange={(e) => setReviewTypes((t) => ({ ...t, pillar: e.target.checked }))}
          />
          Pillar
        </label>
        <label className="flex items-center gap-1.5">
          <input
            type="checkbox"
            checked={reviewTypes.blog}
            onChange={(e) => setReviewTypes((t) => ({ ...t, blog: e.target.checked }))}
          />
          Blog
        </label>
        <label className="flex items-center gap-1.5">
          <input
            type="checkbox"
            checked={reviewTypes.tools}
            onChange={(e) => setReviewTypes((t) => ({ ...t, tools: e.target.checked }))}
          />
          Tools
        </label>
      </div>

      <button
        onClick={handleReview}
        disabled={isReviewing || !(reviewTypes.pillar || reviewTypes.blog || reviewTypes.tools)}
        className="mt-4 rounded-md bg-brand px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-brand-dark disabled:opacity-60"
      >
        {isReviewing ? "Reviewing..." : verdicts ? "Re-run review" : "Run review"}
      </button>

      {reviewError && <p className="mt-4 text-sm text-red-600">{reviewError}</p>}

      {verdicts && (
        <div className="mt-5 space-y-2">
          <p className="text-xs text-muted">
            {approvedCount} approved, {exhaustedCount} exhausted, {verdicts.length} rows reviewed.
          </p>
          {verdicts.map((v) => (
            <VerdictRow key={v.id} verdict={v} />
          ))}
        </div>
      )}

      <div className="mt-6 border-t border-border pt-5">
        <h3 className="text-sm font-semibold text-foreground">9. Export .html files</h3>
        <p className="mt-1 text-xs text-muted">
          Downloads a .zip of the eligible content as standalone .html files. Review
          is optional here — a never-reviewed row is included, only a row reviewed and NOT Approved is excluded.
        </p>

        <label className="mt-3 flex items-center gap-2 text-sm text-foreground">
          <input
            type="checkbox"
            checked={includeRevise}
            onChange={(e) => setIncludeRevise(e.target.checked)}
          />
          Include Revise/Exhausted content in export
        </label>

        <div className="mt-3 flex flex-wrap gap-3">
          <button
            type="button"
            disabled={isExporting}
            onClick={handleExport}
            className="mt-3 rounded-md border border-brand px-3 py-2 text-sm font-semibold text-brand transition-colors hover:bg-brand/5 disabled:opacity-60"
          >
            {isExporting ? "Exporting..." : "Export .html files (.zip)"}
          </button>

          <button
            type="button"
            disabled={isCommitting}
            onClick={handleCommit}
            className="mt-3 rounded-md border border-brand px-3 py-2 text-sm font-semibold text-brand transition-colors hover:bg-brand/5 disabled:opacity-60"
          >
            {isCommitting ? "Committing..." : "Commit to geekatyourspot"}
          </button>
        </div>

        {exportError && <p className="mt-3 text-sm text-red-600">{exportError}</p>}
        {commitError && <p className="mt-3 text-sm text-red-600">{commitError}</p>}

        {commitResult && (
          <div className="mt-3 rounded-md bg-green-50 p-3 text-xs text-green-900">
            <p className="font-medium">
              Committed {commitResult.filePaths.length} file(s) —{" "}
              <a href={commitResult.commitUrl} target="_blank" className="underline">
                view commit
              </a>
            </p>
            <ul className="mt-2 space-y-1 font-mono">
              {commitResult.filePaths.map((path) => (
                <li key={path}>{path}</li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </div>
  );
}

function VerdictRow({ verdict }: { verdict: ReviewVerdict }) {
  const badgeClass =
    verdict.status === "Approved"
      ? "bg-green-100 text-green-800"
      : verdict.status === "Exhausted"
        ? "bg-red-100 text-red-800"
        : "bg-amber-100 text-amber-800";

  let notes = "";
  try {
    const parsed = JSON.parse(verdict.notesJson) as { notes?: string };
    notes = parsed.notes ?? "";
  } catch {
    notes = verdict.notesJson;
  }

  return (
    <div className="rounded-lg border border-border bg-background p-3 text-xs">
      <div className="flex flex-wrap items-center gap-2">
        <span className={`rounded-full px-2 py-0.5 font-medium ${badgeClass}`}>{verdict.status}</span>
        <span className="text-muted">
          attempt {verdict.attemptCount} · reviewed by {verdict.reviewerProvider} ({verdict.reviewerModel})
        </span>
        {verdict.retryCount > 0 && (
          <span className="rounded-full bg-amber-100 px-2 py-0.5 font-medium text-amber-800">
            retried {verdict.retryCount}x
          </span>
        )}
      </div>
      {verdict.retryReason && <p className="mt-2 text-amber-700">{verdict.retryReason}</p>}
      {notes && <p className="mt-2 text-foreground">{notes}</p>}
    </div>
  );
}

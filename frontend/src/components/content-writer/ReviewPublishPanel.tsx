"use client";

import { useEffect, useMemo, useState } from "react";
import {
  commitHtmlExportToGitHub,
  downloadHtmlExport,
  runReview,
  rewriteFromLatestVerdict,
  ApiError,
} from "@/lib/content-writer/api";
import type {
  CommitHtmlExportResult,
  GeneratedContentSet,
  ReviewVerdict,
  ToolPostDraft,
} from "@/lib/content-writer/types";

export default function ReviewPublishPanel({
  projectId,
  result,
  onGenerated,
}: {
  projectId: string;
  result: GeneratedContentSet | null;
  onGenerated: (result: GeneratedContentSet) => void;
}) {
  const toolPosts = result?.toolPosts ?? [];
  const [verdicts, setVerdicts] = useState<ReviewVerdict[] | null>(null);
  const [rewritingId, setRewritingId] = useState<string | null>(null);
  const [rewriteError, setRewriteError] = useState<string | null>(null);
  const [isReviewing, setIsReviewing] = useState(false);
  const [reviewError, setReviewError] = useState<string | null>(null);
  const [reviewTypes, setReviewTypes] = useState({ pillar: true, blog: true, tools: true });
  const [selectedToolSlug, setSelectedToolSlug] = useState<string>("");
  const [selectedRewriteVerdictId, setSelectedRewriteVerdictId] = useState<string>("");

  const [isExporting, setIsExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  const [includeRevise, setIncludeRevise] = useState(true);

  const [isCommitting, setIsCommitting] = useState(false);
  const [commitError, setCommitError] = useState<string | null>(null);
  const [commitResult, setCommitResult] = useState<CommitHtmlExportResult | null>(null);

  const hasPublishableContent = (result?.article?.wordCount ?? 0) > 0 || result?.blog != null;

  useEffect(() => {
    if (toolPosts.length === 0) {
      setSelectedToolSlug("");
      return;
    }
    setSelectedToolSlug((prev) =>
      prev && toolPosts.some((t) => t.slug === prev) ? prev : toolPosts[0].slug,
    );
  }, [toolPosts]);

  const rewriteCandidates = useMemo(
    () => (verdicts ?? []).filter((v) => canRewriteVerdict(v)),
    [verdicts],
  );

  useEffect(() => {
    if (rewriteCandidates.length === 0) {
      setSelectedRewriteVerdictId("");
      return;
    }
    setSelectedRewriteVerdictId((prev) =>
      prev && rewriteCandidates.some((v) => v.id === prev) ? prev : rewriteCandidates[0].id,
    );
  }, [rewriteCandidates]);

  async function handleReview() {
    setReviewError(null);
    setIsReviewing(true);
    try {
      if (reviewTypes.tools && toolPosts.length === 0) {
        throw new ApiError("No tool documents exist to review. Generate tools in Step 6 first.", 400);
      }
      if (reviewTypes.tools && !selectedToolSlug) {
        throw new ApiError("Choose which tool document to review.", 400);
      }

      const contentTypes = [
        reviewTypes.pillar && "TechnicalArticle",
        reviewTypes.blog && "BlogPost",
        reviewTypes.tools && "ToolPost",
      ].filter((t): t is string => Boolean(t));

      const toolSlug = reviewTypes.tools ? selectedToolSlug : null;
      const next = await runReview(projectId, contentTypes, toolSlug);
      setVerdicts(next);
    } catch (err) {
      setReviewError(err instanceof ApiError ? err.message : "Review failed.");
    } finally {
      setIsReviewing(false);
    }
  }

  async function handleRewrite(verdict: ReviewVerdict) {
    setRewriteError(null);
    setRewritingId(verdict.id);
    try {
      const next = await rewriteFromLatestVerdict(projectId, verdict.generatedContentId);
      onGenerated(next);
      // The rewritten content no longer matches the notes that triggered this rewrite — drop the
      // verdict locally so its "Revise" badge can't keep showing next to already-rewritten
      // content. A fresh, real verdict requires clicking "Re-run review" again.
      setVerdicts((prev) => prev?.filter((v) => v.id !== verdict.id) ?? prev);
    } catch (err) {
      setRewriteError(err instanceof ApiError ? err.message : "Rewrite failed.");
    } finally {
      setRewritingId(null);
    }
  }

  async function handleRewriteSelected() {
    const verdict = rewriteCandidates.find((v) => v.id === selectedRewriteVerdictId);
    if (!verdict) {
      setRewriteError("Choose a document to rewrite.");
      return;
    }
    await handleRewrite(verdict);
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
  const reviseCount = verdicts?.filter((v) => v.status === "Revise" || v.status === "Exhausted").length ?? 0;
  const canRunReview =
    !isReviewing &&
    (reviewTypes.pillar || reviewTypes.blog || reviewTypes.tools) &&
    (!reviewTypes.tools || Boolean(selectedToolSlug));

  if (!hasPublishableContent) {
    return null;
  }

  return (
    <div className="rounded-xl border border-border bg-surface p-6 shadow-sm">
      <h2 className="text-lg font-semibold text-foreground">9. Editorial Review</h2>
      <p className="mt-1 text-sm text-muted">
        A different model reviews each selected row (invented-feature/fact check, brand-voice consistency) —
        single pass. At most <span className="font-medium text-foreground">one tool document</span> is reviewed
        per run. When feedback is returned, choose a document and use{" "}
        <span className="font-medium text-foreground">Rewrite with feedback</span> to pass those suggestions to
        the writer. Re-run review afterward to confirm.
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
            disabled={toolPosts.length === 0}
          />
          One tool
        </label>
      </div>

      {reviewTypes.tools && toolPosts.length > 0 && (
        <label className="mt-3 flex flex-col gap-1 text-sm text-foreground sm:max-w-md">
          <span className="text-xs font-medium text-muted">Tool document to review</span>
          <select
            value={selectedToolSlug}
            onChange={(e) => setSelectedToolSlug(e.target.value)}
            className="rounded-md border border-border bg-background px-3 py-2 text-sm"
          >
            {toolPosts.map((tool) => (
              <option key={tool.slug} value={tool.slug}>
                {toolLabel(tool)}
              </option>
            ))}
          </select>
        </label>
      )}

      <button
        onClick={handleReview}
        disabled={!canRunReview}
        className="mt-4 rounded-md bg-brand px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-brand-dark disabled:opacity-60"
      >
        {isReviewing ? "Reviewing..." : verdicts ? "Re-run review" : "Run review"}
      </button>

      {reviewError && <p className="mt-4 text-sm text-red-600">{reviewError}</p>}

      {verdicts && (
        <div className="mt-5 space-y-3">
          <p className="text-xs text-muted">
            {approvedCount} approved, {reviseCount} need rewrite, {verdicts.length} rows reviewed.
          </p>

          {rewriteCandidates.length > 0 && (
            <div className="rounded-lg border border-border bg-background p-3">
              <p className="text-sm font-semibold text-foreground">Rewrite a particular document</p>
              <p className="mt-1 text-xs text-muted">
                Pass that document&apos;s review suggestions to the writer and regenerate only that row.
              </p>
              <div className="mt-3 flex flex-col gap-2 sm:flex-row sm:items-end">
                <label className="flex min-w-0 flex-1 flex-col gap-1 text-sm text-foreground">
                  <span className="text-xs font-medium text-muted">Document</span>
                  <select
                    value={selectedRewriteVerdictId}
                    onChange={(e) => setSelectedRewriteVerdictId(e.target.value)}
                    className="rounded-md border border-border bg-background px-3 py-2 text-sm"
                  >
                    {rewriteCandidates.map((v) => (
                      <option key={v.id} value={v.id}>
                        {rewriteOptionLabel(v)}
                      </option>
                    ))}
                  </select>
                </label>
                <button
                  type="button"
                  onClick={handleRewriteSelected}
                  disabled={rewritingId !== null || !selectedRewriteVerdictId}
                  className="rounded-md border border-brand px-3 py-2 text-sm font-semibold text-brand transition-colors hover:bg-brand/5 disabled:opacity-60"
                >
                  {rewritingId === selectedRewriteVerdictId
                    ? "Rewriting with feedback..."
                    : "Rewrite with feedback"}
                </button>
              </div>
            </div>
          )}

          {verdicts.map((v) => (
            <VerdictRow
              key={v.id}
              verdict={v}
              onRewrite={handleRewrite}
              isRewriting={rewritingId === v.id}
            />
          ))}
          {rewriteError && <p className="text-sm text-red-600">{rewriteError}</p>}
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

function toolLabel(tool: ToolPostDraft): string {
  return `${tool.title} (${tool.slug})`;
}

function extractNotes(notesJson: string): string {
  try {
    const parsed = JSON.parse(notesJson) as { notes?: string };
    return parsed.notes ?? "";
  } catch {
    return notesJson;
  }
}

function canRewriteVerdict(verdict: ReviewVerdict): boolean {
  return (
    (verdict.status === "Revise" || verdict.status === "Exhausted") &&
    extractNotes(verdict.notesJson).trim().length > 0
  );
}

function contentTypeLabel(contentType: ReviewVerdict["contentType"]): string {
  switch (contentType) {
    case "TechnicalArticle":
      return "Pillar";
    case "BlogPost":
      return "Blog";
    case "ToolPost":
      return "Tool";
    default:
      return contentType;
  }
}

function rewriteOptionLabel(verdict: ReviewVerdict): string {
  const type = contentTypeLabel(verdict.contentType);
  return verdict.title ? `${type} · ${verdict.title}` : type;
}

function VerdictRow({
  verdict,
  onRewrite,
  isRewriting,
}: {
  verdict: ReviewVerdict;
  onRewrite: (verdict: ReviewVerdict) => void;
  isRewriting: boolean;
}) {
  const badgeClass =
    verdict.status === "Approved"
      ? "bg-green-100 text-green-800"
      : verdict.status === "Exhausted"
        ? "bg-red-100 text-red-800"
        : "bg-amber-100 text-amber-800";

  const notes = extractNotes(verdict.notesJson);
  const canRewrite = canRewriteVerdict(verdict);

  return (
    <div className="rounded-lg border border-border bg-background p-3 text-xs">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-semibold text-foreground">
          {contentTypeLabel(verdict.contentType)}
          {verdict.title ? ` · ${verdict.title}` : ""}
        </span>
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
      {notes && (
        <div className="mt-2 rounded-md border border-border bg-surface p-2 text-foreground">
          <p className="mb-1 font-medium text-muted">Suggestions for writer</p>
          <p className="whitespace-pre-wrap">{notes}</p>
        </div>
      )}
      {canRewrite && (
        <button
          type="button"
          onClick={() => onRewrite(verdict)}
          disabled={isRewriting}
          className="mt-2 rounded-md border border-brand px-2 py-1 text-xs font-semibold text-brand transition-colors hover:bg-brand/5 disabled:opacity-60"
        >
          {isRewriting ? "Rewriting with feedback..." : "Rewrite with feedback"}
        </button>
      )}
    </div>
  );
}

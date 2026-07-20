"use client";

import { useEffect, useState } from "react";
import { downloadMdxExport, getGeekBackendCategories, publishToGeekBlog, runReview, ApiError } from "@/lib/content-writer/api";
import type { CategoryOption, GeneratedContentSet, PublishResult, ReviewVerdict } from "@/lib/content-writer/types";

export default function ReviewPublishPanel({
  projectId,
  clientId,
  result,
}: {
  projectId: string;
  clientId: string;
  result: GeneratedContentSet | null;
}) {
  const [verdicts, setVerdicts] = useState<ReviewVerdict[] | null>(null);
  const [isReviewing, setIsReviewing] = useState(false);
  const [reviewError, setReviewError] = useState<string | null>(null);

  const [publishResult, setPublishResult] = useState<PublishResult | null>(null);
  const [isPublishing, setIsPublishing] = useState(false);
  const [publishError, setPublishError] = useState<string | null>(null);
  const [publishDepartment, setPublishDepartment] = useState("");
  const [categories, setCategories] = useState<CategoryOption[] | null>(null);
  const [categoriesError, setCategoriesError] = useState<string | null>(null);

  const [isExporting, setIsExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  const hasPublishableContent = (result?.article?.wordCount ?? 0) > 0 || result?.blog != null;

  useEffect(() => {
    let cancelled = false;
    getGeekBackendCategories(clientId)
      .then((options) => {
        if (!cancelled) setCategories(options);
      })
      .catch(() => {
        if (!cancelled) setCategoriesError("Could not load categories — publish is unavailable.");
      });
    return () => {
      cancelled = true;
    };
  }, [clientId]);

  async function handleReview() {
    setReviewError(null);
    setIsReviewing(true);
    try {
      const next = await runReview(projectId);
      setVerdicts(next);
    } catch (err) {
      setReviewError(err instanceof ApiError ? err.message : "Review failed.");
    } finally {
      setIsReviewing(false);
    }
  }

  async function handlePublish() {
    setPublishError(null);
    setPublishResult(null);
    setIsPublishing(true);
    try {
      const next = await publishToGeekBlog(projectId, publishDepartment);
      setPublishResult(next);
    } catch (err) {
      setPublishError(err instanceof ApiError ? err.message : "Publish failed.");
    } finally {
      setIsPublishing(false);
    }
  }

  async function handleExport() {
    setExportError(null);
    setIsExporting(true);
    try {
      await downloadMdxExport(projectId);
    } catch (err) {
      setExportError(err instanceof ApiError ? err.message : "Export failed.");
    } finally {
      setIsExporting(false);
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
        A different model reviews each row (invented-feature/fact check, brand-voice consistency) —
        single pass, no revise-and-retry. Only Approved rows can publish.
      </p>

      <button
        onClick={handleReview}
        disabled={isReviewing}
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
        <h3 className="text-sm font-semibold text-foreground">9. Publish to GeekBackend</h3>
        <p className="mt-1 text-xs text-muted">
          Publishes only rows with the most recent ReviewVerdict Approved — run the review above first.
        </p>

        <label className="mt-3 flex flex-col gap-1 text-xs text-muted">
          Category
          {categoriesError ? (
            <span className="text-red-600">{categoriesError}</span>
          ) : (
            <select
              value={publishDepartment}
              onChange={(e) => setPublishDepartment(e.target.value)}
              disabled={categories === null}
              className="max-w-xs rounded-md border border-border bg-white px-2 py-1.5 text-sm text-foreground outline-none focus:border-brand"
            >
              <option value="">{categories === null ? "Loading categories..." : "Select a category"}</option>
              {categories?.map((c) => (
                <option key={c.slug} value={c.slug}>
                  {c.name ?? c.slug}
                </option>
              ))}
            </select>
          )}
        </label>

        <button
          type="button"
          disabled={isPublishing || !publishDepartment || categoriesError != null}
          onClick={handlePublish}
          className="mt-3 rounded-md border border-brand px-3 py-2 text-sm font-semibold text-brand transition-colors hover:bg-brand/5 disabled:opacity-60"
        >
          {isPublishing ? "Publishing..." : "Publish to GeekBackend"}
        </button>

        {publishError && <p className="mt-3 text-sm text-red-600">{publishError}</p>}

        {publishResult && (
          <div className="mt-3 rounded-md bg-green-50 p-3 text-xs text-green-900">
            <p className="font-medium">
              Published {publishResult.posts.length} post(s) under category{" "}
              <span className="font-mono">{publishResult.categorySlug}</span>
            </p>
            <ul className="mt-2 space-y-1 font-mono">
              {publishResult.posts.map((post) => (
                <li key={post.postId}>
                  {post.contentType}: post #{post.postId} ({post.slug}, {post.languageCode}) —{" "}
                  {post.sectionCount} section(s), {post.wasUpdated ? "updated" : "created"}
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>

      <div className="mt-6 border-t border-border pt-5">
        <h3 className="text-sm font-semibold text-foreground">10. Export .mdx files</h3>
        <p className="mt-1 text-xs text-muted">
          Downloads a .zip of the approved article/blog/tool content as .mdx files (YAML frontmatter + Markdown
          body) — same Approved-verdict gate as publish above.
        </p>

        <button
          type="button"
          disabled={isExporting}
          onClick={handleExport}
          className="mt-3 rounded-md border border-brand px-3 py-2 text-sm font-semibold text-brand transition-colors hover:bg-brand/5 disabled:opacity-60"
        >
          {isExporting ? "Exporting..." : "Export .mdx files"}
        </button>

        {exportError && <p className="mt-3 text-sm text-red-600">{exportError}</p>}
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

"use client";

import { useState } from "react";
import { deleteKeywordSource, uploadKeywordSource, ApiError } from "@/lib/content-writer/api";
import { KEYWORD_SOURCE_CATEGORIES, type KeywordSourceCategory, type KeywordSourceResponse } from "@/lib/content-writer/types";

export default function FileUploadPanel({
  projectId,
  keywordSources,
  onChanged,
}: {
  projectId: string;
  keywordSources: KeywordSourceResponse[];
  onChanged: (sources: KeywordSourceResponse[]) => void;
}) {
  const [category, setCategory] = useState<KeywordSourceCategory>("KeywordResult");
  const [isUploading, setIsUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleFilesSelected(fileList: FileList | null) {
    if (!fileList || fileList.length === 0) return;
    setError(null);
    setIsUploading(true);
    try {
      const uploaded: KeywordSourceResponse[] = [];
      for (const file of Array.from(fileList)) {
        uploaded.push(await uploadKeywordSource(projectId, category, file));
      }
      onChanged([...keywordSources, ...uploaded]);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Upload failed.");
    } finally {
      setIsUploading(false);
    }
  }

  async function handleDelete(id: string) {
    await deleteKeywordSource(projectId, id);
    onChanged(keywordSources.filter((k) => k.id !== id));
  }

  const grouped = KEYWORD_SOURCE_CATEGORIES.map((cat) => ({
    ...cat,
    files: keywordSources.filter((k) => k.category === cat.value),
  }));

  return (
    <div className="rounded-xl border border-border bg-surface p-6 shadow-sm">
      <h2 className="text-lg font-semibold text-foreground">3. Upload Research Inputs</h2>
      <p className="mt-1 text-sm text-muted">
        Upload manually-scraped research by category. Keyword SERP files are kept tight (intent/headings);
        Wikipedia, .edu, and .gov files are used as quotable sources in the article body. PAA: one question per line (top 12 used in the FAQ section).
      </p>

      <div className="mt-5 flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1.5 text-sm font-medium text-foreground">
          Source Category
          <select
            value={category}
            onChange={(e) => setCategory(e.target.value as KeywordSourceCategory)}
            className="rounded-md border border-border bg-white px-3 py-2 text-sm outline-none focus:border-brand focus:ring-2 focus:ring-brand/20"
          >
            {KEYWORD_SOURCE_CATEGORIES.map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </select>
        </label>

        <label className="flex cursor-pointer items-center gap-2 rounded-md border border-dashed border-brand/50 bg-brand/5 px-4 py-2 text-sm font-semibold text-brand hover:bg-brand/10">
          {isUploading ? "Uploading..." : "Choose File(s)"}
          <input
            type="file"
            multiple
            accept={category === "PeopleAlsoAsk" ? ".txt" : ".html,.htm"}
            className="hidden"
            disabled={isUploading}
            onChange={(e) => handleFilesSelected(e.target.files)}
          />
        </label>
      </div>

      {error && <p className="mt-4 text-sm text-red-600">{error}</p>}

      <div className="mt-5 grid gap-3 sm:grid-cols-2">
        {grouped.map((group) => (
          <div key={group.value} className="rounded-lg border border-border bg-background p-3">
            <div className="flex items-center justify-between">
              <span className="text-sm font-semibold text-foreground">{group.label}</span>
              <span className="rounded-full bg-brand/10 px-2 py-0.5 text-xs font-medium text-brand">
                {group.files.length}
              </span>
            </div>
            <ul className="mt-2 space-y-1">
              {group.files.map((file) => (
                <li key={file.id} className="flex items-center justify-between text-xs text-muted">
                  <span className="truncate">{file.originalFileName}</span>
                  <button
                    onClick={() => handleDelete(file.id)}
                    className="ml-2 shrink-0 text-red-500 hover:underline"
                  >
                    Remove
                  </button>
                </li>
              ))}
              {group.files.length === 0 && <li className="text-xs text-muted/70">No files yet</li>}
            </ul>
          </div>
        ))}
      </div>
    </div>
  );
}

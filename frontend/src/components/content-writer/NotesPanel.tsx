"use client";

import { useState } from "react";
import { updateProjectNotes, ApiError } from "@/lib/content-writer/api";
import type { ProjectDetail } from "@/lib/content-writer/types";

export default function NotesPanel({
  projectId,
  notes,
  onSaved,
}: {
  projectId: string;
  notes: string | null;
  onSaved: (project: ProjectDetail) => void;
}) {
  const [value, setValue] = useState(notes ?? "");
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const isDirty = value !== (notes ?? "");

  async function handleSave() {
    setError(null);
    setIsSaving(true);
    setSaved(false);
    try {
      const project = await updateProjectNotes(projectId, value);
      onSaved(project);
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not save notes.");
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <div className="rounded-xl border border-border bg-surface p-6 shadow-sm">
      <h2 className="text-lg font-semibold text-foreground">4. Notes</h2>
      <p className="mt-1 text-sm text-muted">
        Optional. Comma-separated topics that must appear as headings in the pillar article (e.g.
        Pricing, Implementation Timeline, Security &amp; Compliance).
      </p>

      <textarea
        value={value}
        onChange={(e) => {
          setValue(e.target.value);
          setSaved(false);
        }}
        rows={3}
        placeholder="Pricing, Implementation Timeline, Security &amp; Compliance"
        className="mt-4 w-full rounded-md border border-border bg-white px-3 py-2 text-sm outline-none focus:border-brand focus:ring-2 focus:ring-brand/20"
      />

      <div className="mt-3 flex items-center gap-3">
        <button
          onClick={handleSave}
          disabled={isSaving || !isDirty}
          className="rounded-md bg-brand px-4 py-2 text-sm font-semibold text-white hover:bg-brand/90 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {isSaving ? "Saving..." : "Save"}
        </button>
        {saved && !isDirty && <span className="text-sm text-green-600">Saved</span>}
        {error && <span className="text-sm text-red-600">{error}</span>}
      </div>
    </div>
  );
}

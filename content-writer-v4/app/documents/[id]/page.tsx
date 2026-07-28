"use client";

import { useParams, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { DocumentDto } from "@/lib/types";

export default function DocumentEditorPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const [doc, setDoc] = useState<DocumentDto | null>(null);
  const [title, setTitle] = useState("");
  const [content, setContent] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    api
      .getDocument(params.id)
      .then((loaded) => {
        setDoc(loaded);
        setTitle(loaded.title);
        setContent(loaded.content);
      })
      .catch((err: Error) => setError(err.message));
  }, [params.id]);

  async function onSave() {
    if (!doc) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await api.updateDocument(doc.id, {
        title,
        content,
        inputs: doc.inputs,
        brandVoiceId: doc.brandVoiceId,
      });
      setDoc(updated);
      setMessage("Saved.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed");
    } finally {
      setBusy(false);
    }
  }

  async function onRegenerate() {
    if (!doc?.templateId) {
      setError("This document has no template to regenerate from.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const inputs: Record<string, string> = {};
      for (const [key, value] of Object.entries(doc.inputs ?? {})) {
        inputs[key] = String(value ?? "");
      }
      const result = await api.generate({
        templateId: doc.templateId,
        inputs,
        brandVoiceId: doc.brandVoiceId ?? undefined,
        documentId: doc.id,
      });
      setContent(result.output);
      setMessage(
        `Regenerated · ${result.usage.provider}/${result.usage.model} · $${result.usage.costUsd.toFixed(4)}`,
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Regenerate failed");
    } finally {
      setBusy(false);
    }
  }

  async function onDelete() {
    if (!doc) return;
    await api.deleteDocument(doc.id);
    router.push("/documents");
  }

  if (!doc && !error) return <p className="empty">Loading document…</p>;
  if (!doc) return <p className="error">{error}</p>;

  return (
    <section>
      <div className="hero">
        <h1 className="page-title">Edit document</h1>
        <p className="lede">Inline markdown/plain text. Regenerate keeps the original inputs.</p>
      </div>

      <div className="form">
        <div className="field">
          <label htmlFor="title">Title</label>
          <input id="title" value={title} onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div className="field">
          <label htmlFor="content">Content</label>
          <textarea
            id="content"
            className="editor"
            value={content}
            onChange={(e) => setContent(e.target.value)}
          />
        </div>
        {error && <p className="error">{error}</p>}
        {message && <p className="meta">{message}</p>}
        <div className="actions">
          <button className="button" type="button" disabled={busy} onClick={() => void onSave()}>
            Save
          </button>
          <button
            className="button secondary"
            type="button"
            disabled={busy}
            onClick={() => void onRegenerate()}
          >
            Re-generate
          </button>
          <button className="button danger" type="button" disabled={busy} onClick={() => void onDelete()}>
            Delete
          </button>
        </div>
      </div>
    </section>
  );
}

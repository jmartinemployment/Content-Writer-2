"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { BrandVoiceDto } from "@/lib/types";

const emptyForm = {
  name: "",
  description: "",
  tone: "",
  sampleText: "",
};

export default function BrandVoicesPage() {
  const [voices, setVoices] = useState<BrandVoiceDto[]>([]);
  const [form, setForm] = useState(emptyForm);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setVoices(await api.getBrandVoices());
  }

  useEffect(() => {
    refresh().catch((err: Error) => setError(err.message));
  }, []);

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      if (editingId) {
        await api.updateBrandVoice(editingId, form);
      } else {
        await api.createBrandVoice(form);
      }
      setForm(emptyForm);
      setEditingId(null);
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed");
    }
  }

  function startEdit(voice: BrandVoiceDto) {
    setEditingId(voice.id);
    setForm({
      name: voice.name,
      description: voice.description,
      tone: voice.tone,
      sampleText: voice.sampleText,
    });
  }

  async function onDelete(id: string) {
    await api.deleteBrandVoice(id);
    if (editingId === id) {
      setEditingId(null);
      setForm(emptyForm);
    }
    await refresh();
  }

  return (
    <section>
      <div className="hero">
        <h1 className="page-title">Brand voices</h1>
        <p className="lede">
          Save tone profiles and attach them when generating. Same template, different voice.
        </p>
      </div>

      <div className="panel">
        <form className="form" onSubmit={(event) => void onSubmit(event)}>
          <div className="field">
            <label htmlFor="name">Name</label>
            <input
              id="name"
              required
              value={form.name}
              onChange={(e) => setForm((prev) => ({ ...prev, name: e.target.value }))}
            />
          </div>
          <div className="field">
            <label htmlFor="tone">Tone</label>
            <input
              id="tone"
              required
              value={form.tone}
              onChange={(e) => setForm((prev) => ({ ...prev, tone: e.target.value }))}
            />
          </div>
          <div className="field">
            <label htmlFor="description">Description</label>
            <textarea
              id="description"
              required
              value={form.description}
              onChange={(e) => setForm((prev) => ({ ...prev, description: e.target.value }))}
            />
          </div>
          <div className="field">
            <label htmlFor="sampleText">Sample text</label>
            <textarea
              id="sampleText"
              required
              value={form.sampleText}
              onChange={(e) => setForm((prev) => ({ ...prev, sampleText: e.target.value }))}
            />
          </div>
          {error && <p className="error">{error}</p>}
          <div className="actions">
            <button className="button" type="submit">
              {editingId ? "Update voice" : "Create voice"}
            </button>
            {editingId && (
              <button
                className="button secondary"
                type="button"
                onClick={() => {
                  setEditingId(null);
                  setForm(emptyForm);
                }}
              >
                Cancel
              </button>
            )}
          </div>
        </form>

        <div className="list-panel list">
          {voices.length === 0 ? (
            <p className="empty">No brand voices yet.</p>
          ) : (
            voices.map((voice) => (
              <div className="list-item" key={voice.id}>
                <div>
                  <strong>{voice.name}</strong>
                  <p className="meta">{voice.tone}</p>
                  <p>{voice.description}</p>
                </div>
                <div className="actions">
                  <button className="button secondary" type="button" onClick={() => startEdit(voice)}>
                    Edit
                  </button>
                  <button className="button danger" type="button" onClick={() => void onDelete(voice.id)}>
                    Delete
                  </button>
                </div>
              </div>
            ))
          )}
        </div>
      </div>
    </section>
  );
}

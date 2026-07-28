"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { BrandVoiceDto, TemplateDto } from "@/lib/types";

export default function TemplateDetailPage() {
  const params = useParams<{ slug: string }>();
  const slug = params.slug;

  const [template, setTemplate] = useState<TemplateDto | null>(null);
  const [providers, setProviders] = useState<string[]>([]);
  const [voices, setVoices] = useState<BrandVoiceDto[]>([]);
  const [inputs, setInputs] = useState<Record<string, string>>({});
  const [provider, setProvider] = useState("openai");
  const [brandVoiceId, setBrandVoiceId] = useState("");
  const [output, setOutput] = useState("");
  const [usage, setUsage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [savedId, setSavedId] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([api.getTemplate(slug), api.getProviders(), api.getBrandVoices()])
      .then(([tmpl, providerNames, brandVoices]) => {
        setTemplate(tmpl);
        setProviders(providerNames);
        setVoices(brandVoices);
        if (providerNames.length > 0) {
          setProvider(providerNames.includes("openai") ? "openai" : providerNames[0]);
        }
        const defaults: Record<string, string> = {};
        for (const field of tmpl.inputSchema) {
          defaults[field.key] = "";
        }
        setInputs(defaults);
      })
      .catch((err: Error) => setError(err.message));
  }, [slug]);

  const title = useMemo(() => {
    if (!template) return "Untitled";
    return inputs.topic || inputs.product || inputs.offer || template.name;
  }, [inputs, template]);

  async function onGenerate() {
    if (!template) return;
    setBusy(true);
    setError(null);
    setSavedId(null);
    try {
      const result = await api.generate({
        templateId: template.id,
        inputs,
        provider,
        brandVoiceId: brandVoiceId || undefined,
      });
      setOutput(result.output);
      setUsage(
        `${result.usage.provider}/${result.usage.model} · ${result.usage.inputTokens + result.usage.outputTokens} tokens · $${result.usage.costUsd.toFixed(4)}`,
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Generation failed");
    } finally {
      setBusy(false);
    }
  }

  async function onSave() {
    if (!template || !output) return;
    setBusy(true);
    setError(null);
    try {
      const doc = await api.createDocument({
        title,
        content: output,
        inputs,
        templateId: template.id,
        brandVoiceId: brandVoiceId || null,
      });
      setSavedId(doc.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed");
    } finally {
      setBusy(false);
    }
  }

  if (!template && !error) {
    return <p className="empty">Loading template…</p>;
  }

  if (!template) {
    return <p className="error">{error}</p>;
  }

  return (
    <section>
      <div className="hero">
        <p className="category">{template.category}</p>
        <h1 className="page-title">
          {template.icon} {template.name}
        </h1>
        <p className="lede">{template.description}</p>
      </div>

      <div className="panel">
        <form
          className="form"
          onSubmit={(event) => {
            event.preventDefault();
            void onGenerate();
          }}
        >
          {template.inputSchema.map((field) => (
            <div className="field" key={field.key}>
              <label htmlFor={field.key}>
                {field.label}
                {field.required ? " *" : ""}
              </label>
              {field.type === "textarea" ? (
                <textarea
                  id={field.key}
                  value={inputs[field.key] ?? ""}
                  placeholder={field.placeholder ?? ""}
                  required={field.required}
                  onChange={(e) => setInputs((prev) => ({ ...prev, [field.key]: e.target.value }))}
                />
              ) : field.type === "select" ? (
                <select
                  id={field.key}
                  value={inputs[field.key] ?? ""}
                  required={field.required}
                  onChange={(e) => setInputs((prev) => ({ ...prev, [field.key]: e.target.value }))}
                >
                  <option value="">Select…</option>
                  {(field.options ?? []).map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                </select>
              ) : (
                <input
                  id={field.key}
                  value={inputs[field.key] ?? ""}
                  placeholder={field.placeholder ?? ""}
                  required={field.required}
                  onChange={(e) => setInputs((prev) => ({ ...prev, [field.key]: e.target.value }))}
                />
              )}
            </div>
          ))}

          <div className="field">
            <label htmlFor="provider">Provider</label>
            <select id="provider" value={provider} onChange={(e) => setProvider(e.target.value)}>
              {providers.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
            </select>
          </div>

          <div className="field">
            <label htmlFor="brandVoice">Brand voice</label>
            <select
              id="brandVoice"
              value={brandVoiceId}
              onChange={(e) => setBrandVoiceId(e.target.value)}
            >
              <option value="">None</option>
              {voices.map((voice) => (
                <option key={voice.id} value={voice.id}>
                  {voice.name} ({voice.tone})
                </option>
              ))}
            </select>
          </div>

          {error && <p className="error">{error}</p>}

          <div className="actions">
            <button className="button" type="submit" disabled={busy}>
              {busy ? "Working…" : "Generate"}
            </button>
            <button
              className="button secondary"
              type="button"
              disabled={busy || !output}
              onClick={() => void onSave()}
            >
              Save as document
            </button>
          </div>
          {savedId && (
            <p className="meta">
              Saved.{" "}
              <Link href={`/documents/${savedId}`}>Open document</Link>
            </p>
          )}
        </form>

        <div className="output">
          <h2 className="category">Output</h2>
          {output ? <pre>{output}</pre> : <p className="empty">Generate to see copy here.</p>}
          {usage && <p className="meta">{usage}</p>}
        </div>
      </div>
    </section>
  );
}

"use client";

import { useState } from "react";
import { PROVIDER_OPTIONS, type LlmProviderType, type ProjectSummary } from "@/lib/content-writer/types";
import { createProject, ApiError, defaultLlmProvider, isProductionContentWriterApi } from "@/lib/content-writer/api";

export default function ProjectForm({
  clientId,
  onCreated,
}: {
  clientId: string;
  onCreated: (project: ProjectSummary) => void;
}) {
  const [name, setName] = useState("");
  const [projectUrl, setProjectUrl] = useState("");
  const [targetKeyword, setTargetKeyword] = useState("");
  const [preferredProvider, setPreferredProvider] = useState<LlmProviderType>(defaultLlmProvider);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);
    try {
      const project = await createProject({ clientId, name, projectUrl, targetKeyword, preferredProvider });
      onCreated(project);
      setName("");
      setProjectUrl("");
      setTargetKeyword("");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not create project. Is the API running?");
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="rounded-xl border border-border bg-surface p-6 shadow-sm">
      <h2 className="text-lg font-semibold text-foreground">New Project</h2>
      <p className="mt-1 text-sm text-muted">
        Enter the client site to crawl and the primary keyword this content set should target.
      </p>

      <div className="mt-5 grid gap-4 sm:grid-cols-2">
        <label className="flex flex-col gap-1.5 text-sm font-medium text-foreground">
          Project Name
          <input
            required
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Acme HVAC - AI Chatbot Launch"
            className="rounded-md border border-border bg-white px-3 py-2 text-sm outline-none focus:border-brand focus:ring-2 focus:ring-brand/20"
          />
        </label>

        <label className="flex flex-col gap-1.5 text-sm font-medium text-foreground">
          Target Keyword
          <input
            required
            value={targetKeyword}
            onChange={(e) => setTargetKeyword(e.target.value)}
            placeholder="ai chatbot implementation cost"
            className="rounded-md border border-border bg-white px-3 py-2 text-sm outline-none focus:border-brand focus:ring-2 focus:ring-brand/20"
          />
        </label>

        <label className="flex flex-col gap-1.5 text-sm font-medium text-foreground sm:col-span-2">
          Project URL
          <input
            required
            type="url"
            value={projectUrl}
            onChange={(e) => setProjectUrl(e.target.value)}
            placeholder="https://client-site.com"
            className="rounded-md border border-border bg-white px-3 py-2 text-sm outline-none focus:border-brand focus:ring-2 focus:ring-brand/20"
          />
        </label>

        <label className="flex flex-col gap-1.5 text-sm font-medium text-foreground sm:col-span-2">
          LLM Provider
          <select
            value={preferredProvider}
            onChange={(e) => setPreferredProvider(e.target.value as LlmProviderType)}
            className="rounded-md border border-border bg-white px-3 py-2 text-sm outline-none focus:border-brand focus:ring-2 focus:ring-brand/20"
          >
            {PROVIDER_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          {isProductionContentWriterApi() && preferredProvider === "LmStudio" && (
            <span className="text-xs text-amber-700">
              LM Studio only works when the API runs on your machine. Use OpenAI or Anthropic on production.
            </span>
          )}
        </label>
      </div>

      {error && <p className="mt-4 text-sm text-red-600">{error}</p>}

      <button
        type="submit"
        disabled={isSubmitting}
        className="mt-5 rounded-md bg-brand px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-brand-dark disabled:opacity-60"
      >
        {isSubmitting ? "Creating..." : "Create Project"}
      </button>
    </form>
  );
}

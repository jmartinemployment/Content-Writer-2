"use client";

import { useState } from "react";
import { createClient, ApiError } from "@/lib/content-writer/api";
import type { Client } from "@/lib/content-writer/types";

export default function ClientsPanel({
  clients,
  selectedClientId,
  onSelect,
  onCreated,
}: {
  clients: Client[];
  selectedClientId: string | null;
  onSelect: (clientId: string) => void;
  onCreated: (client: Client) => void;
}) {
  const [name, setName] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);
    try {
      const client = await createClient({ name });
      onCreated(client);
      setName("");
      setShowForm(false);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not create client.");
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="rounded-xl border border-border bg-surface p-6 shadow-sm">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold text-foreground">Clients</h2>
        <button
          type="button"
          onClick={() => setShowForm((v) => !v)}
          className="text-sm font-medium text-brand hover:underline"
        >
          {showForm ? "Cancel" : "+ New client"}
        </button>
      </div>

      {showForm && (
        <form onSubmit={handleSubmit} className="mt-4 flex flex-wrap items-end gap-3">
          <label className="flex flex-col gap-1.5 text-sm font-medium text-foreground">
            Client Name
            <input
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Acme Corp"
              className="rounded-md border border-border bg-white px-3 py-2 text-sm outline-none focus:border-brand focus:ring-2 focus:ring-brand/20"
            />
          </label>
          <button
            type="submit"
            disabled={isSubmitting}
            className="rounded-md bg-brand px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-brand-dark disabled:opacity-60"
          >
            {isSubmitting ? "Creating..." : "Create"}
          </button>
        </form>
      )}
      {error && <p className="mt-3 text-sm text-red-600">{error}</p>}

      <div className="mt-4 flex flex-wrap gap-2">
        {clients.length === 0 && <p className="text-sm text-muted">No clients yet — create one to get started.</p>}
        {clients.map((client) => (
          <button
            key={client.id}
            type="button"
            onClick={() => onSelect(client.id)}
            className={`rounded-full px-3 py-1.5 text-sm font-medium transition-colors ${
              selectedClientId === client.id
                ? "bg-brand text-white"
                : "bg-background text-foreground hover:bg-border/50"
            }`}
          >
            {client.name}
            {!client.publishTarget && (
              <span className="ml-1.5 text-xs opacity-70" title="No PublishTarget configured — publish will fail">
                ⚠
              </span>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}

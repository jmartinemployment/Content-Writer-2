"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { UsageSummaryDto } from "@/lib/types";

export default function UsagePage() {
  const [usage, setUsage] = useState<UsageSummaryDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .getUsage()
      .then(setUsage)
      .catch((err: Error) => setError(err.message));
  }, []);

  return (
    <section>
      <div className="hero">
        <h1 className="page-title">Usage</h1>
        <p className="lede">Token and cost totals from persisted generations.</p>
      </div>

      {error && <p className="error">{error}</p>}
      {!usage && !error && <p className="empty">Loading usage…</p>}

      {usage && (
        <>
          <div className="stats">
            <div className="stat">
              Generations
              <strong>{usage.generationCount}</strong>
            </div>
            <div className="stat">
              Input tokens
              <strong>{usage.inputTokens}</strong>
            </div>
            <div className="stat">
              Output tokens
              <strong>{usage.outputTokens}</strong>
            </div>
            <div className="stat">
              Cost (USD)
              <strong>${usage.costUsd.toFixed(4)}</strong>
            </div>
          </div>

          <div className="list-panel list">
            <h2 className="category">By provider</h2>
            {usage.byProvider.length === 0 ? (
              <p className="empty">No generations yet.</p>
            ) : (
              usage.byProvider.map((row) => (
                <div className="list-item" key={row.provider}>
                  <div>
                    <strong>{row.provider}</strong>
                    <p className="meta">
                      {row.generationCount} runs · {row.inputTokens + row.outputTokens} tokens
                    </p>
                  </div>
                  <div>${row.costUsd.toFixed(4)}</div>
                </div>
              ))
            )}
          </div>
        </>
      )}
    </section>
  );
}

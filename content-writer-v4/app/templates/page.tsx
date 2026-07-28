"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { TemplateDto } from "@/lib/types";

export default function TemplatesPage() {
  const [templates, setTemplates] = useState<TemplateDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api
      .getTemplates()
      .then(setTemplates)
      .catch((err: Error) => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  const grouped = useMemo(() => {
    const map = new Map<string, TemplateDto[]>();
    for (const template of templates) {
      const list = map.get(template.category) ?? [];
      list.push(template);
      map.set(template.category, list);
    }
    return [...map.entries()];
  }, [templates]);

  return (
    <section>
      <div className="hero">
        <h1 className="page-title">Templates</h1>
        <p className="lede">Choose a starting shape. The form fields come from the catalog.</p>
      </div>

      {loading && <p className="empty">Loading templates…</p>}
      {error && <p className="error">{error}</p>}

      {grouped.map(([category, items]) => (
        <div key={category}>
          <h2 className="category">{category}</h2>
          <div className="grid">
            {items.map((template) => (
              <Link key={template.id} href={`/templates/${template.slug}`} className="card">
                <div className="icon">{template.icon}</div>
                <h2>{template.name}</h2>
                <p>{template.description}</p>
              </Link>
            ))}
          </div>
        </div>
      ))}
    </section>
  );
}

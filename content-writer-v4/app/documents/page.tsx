"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { DocumentDto } from "@/lib/types";

export default function DocumentsPage() {
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .getDocuments()
      .then(setDocuments)
      .catch((err: Error) => setError(err.message));
  }, []);

  async function onDelete(id: string) {
    await api.deleteDocument(id);
    setDocuments((prev) => prev.filter((doc) => doc.id !== id));
  }

  return (
    <section>
      <div className="hero">
        <h1 className="page-title">Documents</h1>
        <p className="lede">Saved generations you can reopen and edit.</p>
      </div>

      {error && <p className="error">{error}</p>}

      <div className="list-panel list">
        {documents.length === 0 ? (
          <p className="empty">No documents yet. Generate from a template and save.</p>
        ) : (
          documents.map((doc) => (
            <div className="list-item" key={doc.id}>
              <div>
                <Link href={`/documents/${doc.id}`}>
                  <strong>{doc.title}</strong>
                </Link>
                <p className="meta">
                  Updated {new Date(doc.updatedAtUtc).toLocaleString()}
                </p>
              </div>
              <button className="button danger" type="button" onClick={() => void onDelete(doc.id)}>
                Delete
              </button>
            </div>
          ))
        )}
      </div>
    </section>
  );
}

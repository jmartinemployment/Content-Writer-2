"use client";

import Link from "next/link";
import type { ProjectSummary } from "@/lib/content-writer/types";

const STATUS_CLASS: Record<string, string> = {
  Draft: "bg-background text-muted",
  Crawling: "bg-amber-100 text-amber-800",
  ReadyForGeneration: "bg-blue-100 text-blue-800",
  Generating: "bg-blue-100 text-blue-800",
  Completed: "bg-green-100 text-green-800",
  Failed: "bg-red-100 text-red-800",
};

export default function ProjectList({ projects }: { projects: ProjectSummary[] }) {
  if (projects.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border bg-background p-6 text-sm text-muted">
        No projects for this client yet — create one above.
      </div>
    );
  }

  return (
    <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-sm">
      <table className="w-full text-left text-sm">
        <thead className="border-b border-border bg-background text-xs uppercase tracking-wide text-muted">
          <tr>
            <th className="px-4 py-3">Name</th>
            <th className="px-4 py-3">Target Keyword</th>
            <th className="px-4 py-3">Status</th>
            <th className="px-4 py-3">Created</th>
          </tr>
        </thead>
        <tbody>
          {projects.map((project) => (
            <tr key={project.id} className="border-b border-border last:border-0 hover:bg-background/60">
              <td className="px-4 py-3">
                <Link href={`/projects/${project.id}`} className="font-medium text-brand hover:underline">
                  {project.name}
                </Link>
              </td>
              <td className="px-4 py-3 text-muted">{project.targetKeyword}</td>
              <td className="px-4 py-3">
                <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_CLASS[project.status] ?? ""}`}>
                  {project.status}
                </span>
              </td>
              <td className="px-4 py-3 text-muted">{new Date(project.createdAtUtc).toLocaleDateString()}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

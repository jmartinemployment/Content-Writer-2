"use client";

import { useEffect, useState } from "react";
import ClientsPanel from "@/components/content-writer/ClientsPanel";
import ProjectForm from "@/components/content-writer/ProjectForm";
import ProjectList from "@/components/content-writer/ProjectList";
import { getClients, getRecentProjects } from "@/lib/content-writer/api";
import type { Client, ProjectSummary } from "@/lib/content-writer/types";

export default function DashboardPage() {
  const [clients, setClients] = useState<Client[]>([]);
  const [selectedClientId, setSelectedClientId] = useState<string | null>(null);
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([getClients(), getRecentProjects()])
      .then(([clientList, projectList]) => {
        setClients(clientList);
        setProjects(projectList);
        if (clientList.length > 0) setSelectedClientId(clientList[0].id);
      })
      .catch(() => setLoadError("Could not reach the Content Writer API."));
  }, []);

  function handleClientCreated(client: Client) {
    setClients((prev) => [...prev, client]);
    setSelectedClientId(client.id);
  }

  function handleProjectCreated(project: ProjectSummary) {
    setProjects((prev) => [project, ...prev]);
  }

  const clientProjects = projects.filter((p) => p.clientId === selectedClientId);

  return (
    <div className="mx-auto max-w-4xl px-4 py-10 sm:px-6 lg:px-8">
      <div className="mb-8">
        <p className="text-sm font-semibold uppercase tracking-wide text-brand">Content Writer v2</p>
        <h1 className="mt-1 text-3xl font-bold text-foreground">Dashboard</h1>
        <p className="mt-2 max-w-2xl text-sm text-muted">
          Crawl a client site, upload research, generate a pillar article + companion content, run editorial
          review, and publish straight to GeekBackend.
        </p>
      </div>

      {loadError && <p className="mb-6 text-sm text-red-600">{loadError}</p>}

      <div className="flex flex-col gap-6">
        <ClientsPanel
          clients={clients}
          selectedClientId={selectedClientId}
          onSelect={setSelectedClientId}
          onCreated={handleClientCreated}
        />

        {selectedClientId && (
          <>
            <ProjectForm clientId={selectedClientId} onCreated={handleProjectCreated} />
            <ProjectList projects={clientProjects} />
          </>
        )}
      </div>
    </div>
  );
}

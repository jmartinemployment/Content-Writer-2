import type {
  CategoryOption,
  Client,
  CommitHtmlExportResult,
  CrawlSummary,
  GeneratedContentSet,
  KeywordSourceCategory,
  KeywordSourceResponse,
  LlmProviderType,
  LmStudioHealthStatus,
  ProjectDetail,
  ProjectSummary,
  ReviewVerdict,
} from "./types";

const API_BASE_URL =
  process.env.NEXT_PUBLIC_CONTENT_WRITER_API_URL ??
  (process.env.NODE_ENV === "production"
    ? "https://backend-production-c5950.up.railway.app"
    : "http://localhost:5052");

/** True when the UI talks to the hosted Railway API (LM Studio is not available there). */
export function isProductionContentWriterApi(): boolean {
  try {
    const url = new URL(API_BASE_URL);
    return url.hostname !== "localhost" && url.hostname !== "127.0.0.1";
  } catch {
    return false;
  }
}

export function defaultLlmProvider(): LlmProviderType {
  return isProductionContentWriterApi() ? "OpenAi" : "LmStudio";
}

export class ApiError extends Error {
  constructor(message: string, public status: number) {
    super(message);
    this.name = "ApiError";
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: {
        ...(init?.body && !(init.body instanceof FormData) ? { "Content-Type": "application/json" } : {}),
        ...init?.headers,
      },
    });
  } catch {
    throw new ApiError(
      `Could not reach the API at ${API_BASE_URL}. Hard-refresh the page and confirm the API is running.`,
      0
    );
  }

  if (!response.ok) {
    const detail = await response.text().catch(() => response.statusText);
    const problemDetail = tryParseProblemDetail(detail);
    throw new ApiError(problemDetail || detail || response.statusText, response.status);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

// --- Clients ---

export function getClients(): Promise<Client[]> {
  return request<Client[]>("/api/clients");
}

export function createClient(input: { name: string; notes?: string }): Promise<Client> {
  return request<Client>("/api/clients", { method: "POST", body: JSON.stringify(input) });
}

export function setPublishTarget(
  clientId: string,
  input: { geekBackendApiBaseUrl: string; apiKeyEnvVar: string; defaultAuthorId: number | null; categoryStrategy: string }
): Promise<Client> {
  return request<Client>(`/api/clients/${clientId}/publish-target`, {
    method: "PUT",
    body: JSON.stringify(input),
  });
}

// --- Projects ---

export function createProject(input: {
  clientId: string;
  name: string;
  projectUrl: string;
  targetKeyword: string;
  department: string;
  preferredProvider: LlmProviderType;
}): Promise<ProjectSummary> {
  return request<ProjectSummary>("/api/projects", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function getRecentProjects(): Promise<ProjectSummary[]> {
  return request<ProjectSummary[]>("/api/projects");
}

export function getProject(projectId: string): Promise<ProjectDetail> {
  return request<ProjectDetail>(`/api/projects/${projectId}`);
}

export function crawlProject(projectId: string, maxPages = 50): Promise<CrawlSummary> {
  return request<CrawlSummary>(`/api/projects/${projectId}/crawl?maxPages=${maxPages}`, {
    method: "POST",
  });
}

export function uploadKeywordSource(
  projectId: string,
  category: KeywordSourceCategory,
  file: File
): Promise<KeywordSourceResponse> {
  const formData = new FormData();
  formData.append("category", category);
  formData.append("file", file);

  return request<KeywordSourceResponse>(`/api/projects/${projectId}/keyword-sources`, {
    method: "POST",
    body: formData,
  });
}

export function deleteKeywordSource(projectId: string, keywordSourceId: string): Promise<void> {
  return request<void>(`/api/projects/${projectId}/keyword-sources/${keywordSourceId}`, {
    method: "DELETE",
  });
}

export function generatePillarPlanContent(projectId: string): Promise<GeneratedContentSet> {
  return request<GeneratedContentSet>(`/api/projects/${projectId}/generate/pillar/plan`, { method: "POST" });
}

export function generatePillarBodyContent(projectId: string): Promise<GeneratedContentSet> {
  return request<GeneratedContentSet>(`/api/projects/${projectId}/generate/pillar/body`, { method: "POST" });
}

export function generateToolsContent(projectId: string): Promise<GeneratedContentSet> {
  return request<GeneratedContentSet>(`/api/projects/${projectId}/generate/tools`, { method: "POST" });
}

export function generateBlogContent(projectId: string): Promise<GeneratedContentSet> {
  return request<GeneratedContentSet>(`/api/projects/${projectId}/generate/blog`, { method: "POST" });
}

export function generateSocialContent(projectId: string): Promise<GeneratedContentSet> {
  return request<GeneratedContentSet>(`/api/projects/${projectId}/generate/social`, { method: "POST" });
}

export function generateColdOutreachContent(projectId: string): Promise<GeneratedContentSet> {
  return request<GeneratedContentSet>(`/api/projects/${projectId}/generate/email-cold-outreach`, {
    method: "POST",
  });
}

export function generateImagePromptsContent(projectId: string): Promise<GeneratedContentSet> {
  return request<GeneratedContentSet>(`/api/projects/${projectId}/generate/image-prompts`, {
    method: "POST",
  });
}

export function generateAllContent(projectId: string): Promise<GeneratedContentSet> {
  return request<GeneratedContentSet>(`/api/projects/${projectId}/generate`, { method: "POST" });
}

export function runReview(projectId: string, contentTypes?: string[], toolSlugToTest?: string | null): Promise<ReviewVerdict[]> {
  const body: { contentTypes?: string[]; toolSlugToTest?: string } = {};
  if (contentTypes?.length) body.contentTypes = contentTypes;
  if (toolSlugToTest) body.toolSlugToTest = toolSlugToTest;
  return request<ReviewVerdict[]>(`/api/projects/${projectId}/review`, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

export async function getGeekBackendCategories(clientId: string, lang = "en"): Promise<CategoryOption[]> {
  const response = await fetch(`${API_BASE_URL}/api/categories?clientId=${clientId}&lang=${lang}`);
  if (!response.ok) {
    throw new ApiError(`Could not load categories from GeekBackend (${response.status}).`, response.status);
  }
  return (await response.json()) as CategoryOption[];
}

export async function downloadHtmlExport(projectId: string, includeRevise: boolean = true): Promise<void> {
  let response: Response;
  try {
    const params = new URLSearchParams();
    if (!includeRevise) params.append("includeRevise", "false");
    const queryString = params.toString();
    const url = `${API_BASE_URL}/api/projects/${projectId}/export/html${queryString ? `?${queryString}` : ""}`;
    response = await fetch(url);
  } catch {
    throw new ApiError(
      `Could not reach the API at ${API_BASE_URL}. Hard-refresh the page and confirm the API is running.`,
      0
    );
  }

  if (!response.ok) {
    const detail = await response.text().catch(() => response.statusText);
    const problemDetail = tryParseProblemDetail(detail);
    throw new ApiError(problemDetail || detail || response.statusText, response.status);
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `${projectId}-html-export.zip`;
  link.click();
  URL.revokeObjectURL(url);
}

export function commitHtmlExportToGitHub(projectId: string, includeRevise: boolean = true): Promise<CommitHtmlExportResult> {
  const params = new URLSearchParams();
  if (!includeRevise) params.append("includeRevise", "false");
  const queryString = params.toString();
  const url = `/api/projects/${projectId}/export/html/commit${queryString ? `?${queryString}` : ""}`;
  return request<CommitHtmlExportResult>(url, { method: "POST" });
}

export function getLmStudioStatus(): Promise<LmStudioHealthStatus> {
  return request<LmStudioHealthStatus>("/api/llm/lm-studio/status");
}

function tryParseProblemDetail(raw: string): string | null {
  try {
    const parsed = JSON.parse(raw) as { detail?: string; title?: string };
    return parsed.detail ?? parsed.title ?? null;
  } catch {
    return null;
  }
}

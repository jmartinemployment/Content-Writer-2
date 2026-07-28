import type {
  BrandVoiceDto,
  DocumentDto,
  GenerateContentResponse,
  TemplateDto,
  UsageSummaryDto,
} from "./types";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";
const DEV_USER_ID =
  process.env.NEXT_PUBLIC_DEV_USER_ID ?? "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${DEV_USER_ID}`,
      ...(init?.headers ?? {}),
    },
    cache: "no-store",
  });

  if (!response.ok) {
    let detail = response.statusText;
    try {
      const body = await response.json();
      detail = typeof body === "object" ? JSON.stringify(body) : String(body);
    } catch {
      /* ignore */
    }
    throw new Error(`API ${response.status}: ${detail}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const api = {
  getTemplates: () => request<TemplateDto[]>("/api/content-writer/v4/templates"),
  getTemplate: (slug: string) =>
    request<TemplateDto>(`/api/content-writer/v4/templates/${encodeURIComponent(slug)}`),
  getProviders: () => request<string[]>("/api/content-writer/v4/providers"),
  generate: (body: {
    templateId: string;
    inputs: Record<string, string>;
    provider?: string;
    brandVoiceId?: string;
    documentId?: string;
  }) =>
    request<GenerateContentResponse>("/api/content-writer/v4/generate", {
      method: "POST",
      body: JSON.stringify(body),
    }),
  getDocuments: () => request<DocumentDto[]>("/api/content-writer/v4/documents"),
  getDocument: (id: string) =>
    request<DocumentDto>(`/api/content-writer/v4/documents/${id}`),
  createDocument: (body: {
    title: string;
    content: string;
    inputs: Record<string, unknown>;
    templateId?: string | null;
    brandVoiceId?: string | null;
  }) =>
    request<DocumentDto>("/api/content-writer/v4/documents", {
      method: "POST",
      body: JSON.stringify(body),
    }),
  updateDocument: (
    id: string,
    body: {
      title: string;
      content: string;
      inputs: Record<string, unknown>;
      brandVoiceId?: string | null;
    },
  ) =>
    request<DocumentDto>(`/api/content-writer/v4/documents/${id}`, {
      method: "PUT",
      body: JSON.stringify(body),
    }),
  deleteDocument: (id: string) =>
    request<void>(`/api/content-writer/v4/documents/${id}`, { method: "DELETE" }),
  getBrandVoices: () => request<BrandVoiceDto[]>("/api/content-writer/v4/brand-voices"),
  createBrandVoice: (body: {
    name: string;
    description: string;
    tone: string;
    sampleText: string;
  }) =>
    request<BrandVoiceDto>("/api/content-writer/v4/brand-voices", {
      method: "POST",
      body: JSON.stringify(body),
    }),
  updateBrandVoice: (
    id: string,
    body: { name: string; description: string; tone: string; sampleText: string },
  ) =>
    request<BrandVoiceDto>(`/api/content-writer/v4/brand-voices/${id}`, {
      method: "PUT",
      body: JSON.stringify(body),
    }),
  deleteBrandVoice: (id: string) =>
    request<void>(`/api/content-writer/v4/brand-voices/${id}`, { method: "DELETE" }),
  getUsage: () => request<UsageSummaryDto>("/api/content-writer/v4/usage"),
};

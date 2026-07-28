export type TemplateFieldSchema = {
  key: string;
  label: string;
  type: "text" | "textarea" | "select" | string;
  options?: string[] | null;
  placeholder?: string | null;
  required: boolean;
};

export type TemplateDto = {
  id: string;
  slug: string;
  name: string;
  description: string;
  category: string;
  icon: string;
  inputSchema: TemplateFieldSchema[];
  systemPrompt: string;
  userPromptTemplate: string;
  isActive: boolean;
  createdAtUtc: string;
};

export type BrandVoiceDto = {
  id: string;
  ownerId: string;
  name: string;
  description: string;
  tone: string;
  sampleText: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type DocumentDto = {
  id: string;
  ownerId: string;
  templateId?: string | null;
  brandVoiceId?: string | null;
  title: string;
  inputs: Record<string, unknown>;
  content: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type GenerationUsageDto = {
  provider: string;
  model: string;
  inputTokens: number;
  outputTokens: number;
  costUsd: number;
};

export type GenerateContentResponse = {
  output: string;
  usage: GenerationUsageDto;
  generationId: string;
};

export type UsageByProviderDto = {
  provider: string;
  generationCount: number;
  inputTokens: number;
  outputTokens: number;
  costUsd: number;
};

export type UsageSummaryDto = {
  generationCount: number;
  inputTokens: number;
  outputTokens: number;
  costUsd: number;
  byProvider: UsageByProviderDto[];
};

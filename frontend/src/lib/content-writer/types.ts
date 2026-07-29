export type LlmProviderType = "LmStudio" | "OpenAi" | "Anthropic" | "Groq";

export type ProjectStatus =
  | "Draft"
  | "Crawling"
  | "ReadyForGeneration"
  | "Generating"
  | "Completed"
  | "Failed";

export type KeywordSourceCategory =
  | "KeywordResult"
  | "EduDomain"
  | "GovDomain"
  | "Wikipedia"
  | "Local"
  | "PeopleAlsoAsk"
  | "CompetitorCrawl";

export type GeneratedContentType =
  | "TechnicalArticle"
  | "BlogPost"
  | "SocialFacebook"
  | "SocialLinkedIn"
  | "EmailColdOutreach"
  | "EmailNewsletter"
  | "EmailStoryNurture"
  | "EmailTransactional"
  | "ImagePromptPillarFigure"
  | "ImagePromptSocialFacebook"
  | "ImagePromptSocialLinkedIn"
  | "ImagePromptSection"
  | "ToolPost";

export type CategoryStrategy = "DepartmentBased" | "FreeForm";

export type ReviewVerdictStatus = "Approved" | "Revise" | "Exhausted";

export interface PublishTarget {
  id: string;
  geekBackendApiBaseUrl: string;
  apiKeyEnvVar: string;
  defaultAuthorId: number | null;
  categoryStrategy: CategoryStrategy;
}

export interface Client {
  id: string;
  name: string;
  notes: string | null;
  createdAtUtc: string;
  publishTarget: PublishTarget | null;
}

export interface ProjectSummary {
  id: string;
  clientId: string;
  name: string;
  projectUrl: string;
  targetKeyword: string;
  department: string;
  status: ProjectStatus;
  preferredProvider: LlmProviderType;
  useExactKeywordAsTitle: boolean;
  createdAtUtc: string;
}

export interface CrawlSummary {
  siteName: string;
  pagesCrawled: number;
  detectedTone: string;
  detectedFocus: string;
  headingCount: number;
  paragraphCount: number;
  jsonLdBlockCount: number;
}

export interface KeywordSourceResponse {
  id: string;
  category: KeywordSourceCategory;
  originalFileName: string;
  extractedTitle: string | null;
  headingCount: number;
  paragraphCount: number;
  questionCount: number;
}

export interface GeneratedContentResponse {
  id: string;
  contentType: GeneratedContentType;
  title: string;
  slug: string;
  metaDescription: string | null;
  keywords: string[];
  wordCount: number;
  bodyHtml: string;
  jsonLdSchema: string | null;
  relatedArticleUrl: string | null;
  createdAtUtc: string;
  noResearchWarning: string | null;
  gaps: string[];
}

export interface ProjectDetail extends ProjectSummary {
  crawl: CrawlSummary | null;
  keywordSources: KeywordSourceResponse[];
  generatedContent: GeneratedContentResponse[];
  contentSet: GeneratedContentSet | null;
  notes: string | null;
}

export interface ArticleDraft {
  title: string;
  metaDescription: string;
  bodyHtml: string;
  keywords: string[];
  wordCount: number;
  sectionOutline: string[];
}

export const CONTENT_LENGTH_TARGETS = {
  pillar: {
    min: 3000,
    max: 5000,
    label: "3,000–5,000+",
    definition:
      "Exhaustive macro-level entry points for massive topics — multiple subsections that link out to cluster articles.",
  },
  blog: {
    min: 1800,
    max: 2500,
    label: "1,800–2,500",
    definition:
      "Deep-dive articles aimed at outranking competitors — substantive depth in every section, not surface summaries.",
  },
  emailColdOutreach: {
    min: 50,
    max: 125,
    label: "50–125",
    definition: "High response rates; pitch a single, clear call-to-action.",
  },
  tools: {
    min: 1500,
    max: 2500,
    label: "1,500–2,500",
    definition:
      "Comprehensive single-platform guides — deep implementation context, capabilities, and when to use it.",
  },
  imagePrompt: {
    min: 20,
    max: 400,
    label: "20–400",
  },
  socialFacebook: {
    minWords: 30,
    maxWords: 50,
    maxChars: 250,
    label: "~40 words, under 250 chars",
  },
  socialLinkedIn: {
    minWords: 200,
    maxWords: 300,
    minChars: 1300,
    maxChars: 1900,
    label: "200–300 words, 1,300–1,900 chars",
  },
} as const;

export interface ColdOutreachEmailDraft {
  subject: string;
  bodyText: string;
  ctaLabel: string;
  ctaUrl: string;
}

export interface SocialPostDraft {
  platform: string;
  text: string;
}

export interface ImagePromptSection {
  sourceType: "pillar-hero" | "blog-hero" | "pillar" | "blog" | "tool";
  heading: string;
  order: number;
  prompt: string;
  width: number;
  height: number;
  imageModel: string;
  imageModelId: string;
  stylePreset: string;
  alchemy: boolean;
  photoReal: boolean;
  notes: string | null;
}

export interface ImagePromptsSet {
  sections: ImagePromptSection[];
}

export interface CategoryOption {
  id: number;
  slug: string;
  name: string | null;
}

export interface ToolPostDraft {
  title: string;
  slug: string;
  toolUrl: string;
  bodyHtml: string;
  metaDescription: string;
  jsonLdSchema: string | null;
  sourceAppOrder: number | null;
  wordCount: number;
}

export interface CommitHtmlExportResult {
  commitSha: string;
  commitUrl: string;
  filePaths: string[];
}

export interface GeneratedContentSet {
  article: ArticleDraft | null;
  articleSlug: string | null;
  articleUrl: string | null;
  articleJsonLd: string | null;
  blog: ArticleDraft | null;
  blogSlug: string | null;
  blogUrl: string | null;
  blogJsonLd: string | null;
  facebookPost: SocialPostDraft | null;
  linkedInPost: SocialPostDraft | null;
  coldOutreachEmail: ColdOutreachEmailDraft | null;
  imagePrompts: ImagePromptsSet | null;
  toolPosts: ToolPostDraft[] | null;
  articleNoResearchWarning: string | null;
  articleGaps: string[] | null;
}

export interface ReviewVerdict {
  id: string;
  generatedContentId: string;
  contentType: GeneratedContentType;
  title: string;
  slug: string;
  status: ReviewVerdictStatus;
  attemptCount: number;
  reviewerProvider: LlmProviderType;
  reviewerModel: string;
  notesJson: string;
  retryCount: number;
  retryReason: string | null;
  createdAtUtc: string;
}

export interface LmStudioHealthStatus {
  isReachable: boolean;
  modelId: string | null;
  message: string | null;
}

export const KEYWORD_SOURCE_CATEGORIES: { value: KeywordSourceCategory; label: string }[] = [
  { value: "KeywordResult", label: "Keyword SERP Result" },
  { value: "EduDomain", label: ".edu Domain" },
  { value: "GovDomain", label: ".gov Domain" },
  { value: "Wikipedia", label: "Wikipedia" },
  { value: "Local", label: "Local Pack" },
  { value: "CompetitorCrawl", label: "Competitor Crawl" },
  { value: "PeopleAlsoAsk", label: "People Also Ask (text)" },
];

export const PROVIDER_OPTIONS: { value: LlmProviderType; label: string }[] = [
  { value: "LmStudio", label: "LM Studio (local dev only)" },
  { value: "OpenAi", label: "OpenAI" },
  { value: "Anthropic", label: "Anthropic (Claude)" },
  { value: "Groq", label: "Groq (Llama)" },
];

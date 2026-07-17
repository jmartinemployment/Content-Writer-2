export type LlmProviderType = "LmStudio" | "OpenAi" | "Anthropic";

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
  status: ProjectStatus;
  preferredProvider: LlmProviderType;
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
}

export interface ProjectDetail extends ProjectSummary {
  crawl: CrawlSummary | null;
  keywordSources: KeywordSourceResponse[];
  generatedContent: GeneratedContentResponse[];
  contentSet: GeneratedContentSet | null;
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
  sourceType: "pillar" | "blog";
  heading: string;
  order: number;
  prompt: string;
  width: number;
  height: number;
  leonardoModel: string;
  leonardoModelId: string;
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
}

export interface PublishedPost {
  contentType: string;
  postId: number;
  slug: string;
  languageCode: string;
  sectionCount: number;
  wasUpdated: boolean;
}

export interface PublishResult {
  categorySlug: string;
  posts: PublishedPost[];
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
}

export interface ReviewVerdict {
  id: string;
  generatedContentId: string;
  status: ReviewVerdictStatus;
  attemptCount: number;
  reviewerProvider: LlmProviderType;
  reviewerModel: string;
  notesJson: string;
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
];

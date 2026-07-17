"use client";

import { useState } from "react";
import {
  generateBlogContent,
  generateColdOutreachContent,
  generateImagePromptsContent,
  generatePillarBodyContent,
  generatePillarPlanContent,
  generateSocialContent,
  generateToolsContent,
  ApiError,
} from "@/lib/content-writer/api";
import type { ColdOutreachEmailDraft, GeneratedContentSet, ImagePromptSection, ImagePromptsSet, ToolPostDraft } from "@/lib/content-writer/types";
import { CONTENT_LENGTH_TARGETS } from "@/lib/content-writer/types";

type Tab = "article" | "blog" | "facebook" | "linkedin" | "cold-outreach" | "tools" | "image-prompts";
type GeneratingStep = "pillar-plan" | "pillar-body" | "blog" | "social" | "cold-outreach" | "tools" | "image-prompts" | "all" | null;

const PILLAR_BODY_MIN_WORDS = 200;

const TABS: { id: Tab; label: string }[] = [
  { id: "article", label: "Technical Article" },
  { id: "blog", label: "Blog Post" },
  { id: "facebook", label: "Facebook" },
  { id: "linkedin", label: "LinkedIn" },
  { id: "cold-outreach", label: "Cold Outreach" },
  { id: "tools", label: "Tools" },
  { id: "image-prompts", label: "Image Prompts" },
];

export default function ContentResults({
  projectId,
  canGenerate,
  result,
  onGenerated,
}: {
  projectId: string;
  canGenerate: boolean;
  result: GeneratedContentSet | null;
  onGenerated: (result: GeneratedContentSet) => void;
}) {
  const [activeTab, setActiveTab] = useState<Tab>("article");
  const [generatingStep, setGeneratingStep] = useState<GeneratingStep>(null);
  const [error, setError] = useState<string | null>(null);

  const hasPillarPlan = result?.article != null;
  const hasPillarBody = (result?.article?.wordCount ?? 0) >= PILLAR_BODY_MIN_WORDS;
  const hasBlog = result?.blog != null;
  const hasSocial = result?.facebookPost != null && result?.linkedInPost != null;
  const hasTools = (result?.toolPosts?.length ?? 0) > 0;
  const hasImagePrompts = (result?.imagePrompts?.sections?.length ?? 0) > 0;
  const isGenerating = generatingStep !== null;

  async function runStep(step: GeneratingStep, action: () => Promise<GeneratedContentSet>) {
    setError(null);
    setGeneratingStep(step);
    try {
      const next = await action();
      onGenerated(next);
      if ((step === "cold-outreach" || step === "all") && next.coldOutreachEmail) {
        setActiveTab("cold-outreach");
      }
      if ((step === "tools" || step === "all") && (next.toolPosts?.length ?? 0) > 0) {
        setActiveTab("tools");
      }
      if ((step === "image-prompts" || step === "all") && (next.imagePrompts?.sections?.length ?? 0) > 0) {
        setActiveTab("image-prompts");
      }
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Generation failed. Check the API logs.";
      setError(message);
    } finally {
      setGeneratingStep(null);
    }
  }

  return (
    <div className="rounded-xl border border-border bg-surface p-6 shadow-sm">
      <h2 className="text-lg font-semibold text-foreground">4. Generate Content</h2>
      <p className="mt-1 text-sm text-muted">
        Run each step separately. Steps 1–2 plan and write the pillar article; steps 3–7 build blog, social, email, tool documents, and Leonardo image prompts from it.
      </p>

      <div className="mt-5 space-y-3">
        <StepRow
          step={1}
          title="Pillar plan (Technical Article)"
          description="Title, meta, keywords, and declarative H2 outline (PAA goes in a final FAQ section only)."
          done={hasPillarPlan}
          disabled={!canGenerate || isGenerating}
          isRunning={generatingStep === "pillar-plan"}
          buttonLabel={hasPillarPlan ? "Regenerate plan" : "Generate plan"}
          onClick={() => runStep("pillar-plan", () => generatePillarPlanContent(projectId))}
        />

        <StepRow
          step={2}
          title="Pillar body"
          description={`${CONTENT_LENGTH_TARGETS.pillar.definition} Target ${CONTENT_LENGTH_TARGETS.pillar.label} words — one outline section per LLM call.`}
          done={hasPillarBody}
          disabled={!hasPillarPlan || isGenerating}
          isRunning={generatingStep === "pillar-body"}
          buttonLabel={hasPillarBody ? "Regenerate body" : "Write body"}
          onClick={() => runStep("pillar-body", () => generatePillarBodyContent(projectId))}
          lockedMessage={!hasPillarPlan ? "Complete Step 1 first." : undefined}
        />

        <StepRow
          step={3}
          title="Blog content"
          description={`${CONTENT_LENGTH_TARGETS.blog.definition} Target ${CONTENT_LENGTH_TARGETS.blog.label} words — one section per LLM call, cross-linked to the pillar.`}
          done={hasBlog}
          disabled={!hasPillarBody || isGenerating}
          isRunning={generatingStep === "blog"}
          buttonLabel={hasBlog ? "Regenerate blog" : "Generate blog"}
          onClick={() => runStep("blog", () => generateBlogContent(projectId))}
          lockedMessage={!hasPillarBody ? "Complete Step 2 first." : undefined}
        />

        <StepRow
          step={4}
          title="Social content"
          description="Facebook (~40 words) and LinkedIn (~200–300 words) posts linking to the pillar."
          done={hasSocial}
          disabled={!hasPillarBody || isGenerating}
          isRunning={generatingStep === "social"}
          buttonLabel={hasSocial ? "Regenerate social" : "Generate social"}
          onClick={() => runStep("social", () => generateSocialContent(projectId))}
          lockedMessage={!hasPillarBody ? "Complete Step 2 first." : undefined}
        />

        <StepRow
          step={5}
          title="Cold outreach email"
          description={`${CONTENT_LENGTH_TARGETS.emailColdOutreach.definition} Target ${CONTENT_LENGTH_TARGETS.emailColdOutreach.label} words — subject, body, and one CTA to the pillar.`}
          done={result?.coldOutreachEmail != null}
          disabled={!hasPillarBody || isGenerating}
          isRunning={generatingStep === "cold-outreach"}
          buttonLabel={result?.coldOutreachEmail ? "Regenerate email" : "Generate email"}
          onClick={() => runStep("cold-outreach", () => generateColdOutreachContent(projectId))}
          lockedMessage={!hasPillarBody ? "Complete Step 2 first." : undefined}
        />

        <StepRow
          step={6}
          title="Tool documents"
          description="Up to 5 standalone pages, one per tool listed in the pillar's AI Tool Section, each with its own SoftwareApplication JSON+LD."
          done={hasTools}
          disabled={!hasPillarBody || isGenerating}
          isRunning={generatingStep === "tools"}
          buttonLabel={hasTools ? "Regenerate tools" : "Generate tools"}
          onClick={() => runStep("tools", () => generateToolsContent(projectId))}
          lockedMessage={!hasPillarBody ? "Complete Step 2 first." : undefined}
        />

        <StepRow
          step={7}
          title="Image prompts (Leonardo)"
          description="One Leonardo.ai prompt per H2 in the pillar and blog — copy each into Leonardo for section figures."
          done={hasImagePrompts}
          disabled={!hasBlog || isGenerating}
          isRunning={generatingStep === "image-prompts"}
          buttonLabel={hasImagePrompts ? "Regenerate prompts" : "Generate prompts"}
          onClick={() => runStep("image-prompts", () => generateImagePromptsContent(projectId))}
          lockedMessage={!hasBlog ? "Complete Step 3 (blog) first." : undefined}
        />
      </div>

      <button
        onClick={() =>
          runStep("all", async () => {
            let state = result;
            if (!state?.article) {
              state = await generatePillarPlanContent(projectId);
              onGenerated(state);
            }
            if ((state.article?.wordCount ?? 0) < PILLAR_BODY_MIN_WORDS) {
              state = await generatePillarBodyContent(projectId);
              onGenerated(state);
            }
            if (!state.blog) {
              state = await generateBlogContent(projectId);
              onGenerated(state);
            }
            if (!state.facebookPost || !state.linkedInPost) {
              state = await generateSocialContent(projectId);
              onGenerated(state);
            }
            if (!state.coldOutreachEmail) {
              state = await generateColdOutreachContent(projectId);
              onGenerated(state);
            }
            if (!state.toolPosts?.length) {
              state = await generateToolsContent(projectId);
              onGenerated(state);
            }
            if (!state.imagePrompts?.sections?.length) {
              state = await generateImagePromptsContent(projectId);
              onGenerated(state);
            }
            return state!;
          })
        }
        disabled={
          !canGenerate ||
          isGenerating ||
          (hasPillarBody && hasBlog && hasSocial && result?.coldOutreachEmail != null && hasTools && hasImagePrompts)
        }
        className="mt-4 text-sm font-medium text-brand hover:underline disabled:opacity-60"
      >
        {generatingStep === "all" ? "Generating..." : "Generate all remaining steps"}
      </button>

      {!canGenerate && (
        <p className="mt-2 text-xs text-muted">Crawl the site and upload at least one research input first.</p>
      )}
      {error && (
        <p className={`mt-4 text-sm ${error.toLowerCase().includes("timed out") ? "text-amber-700" : "text-red-600"}`}>
          {error}
        </p>
      )}

      {result?.article && (
        <div className="mt-6">
          <div className="flex gap-1 border-b border-border">
            {TABS.map((tab) => {
              const unavailable =
                (tab.id === "blog" && !result.blog) ||
                ((tab.id === "facebook" || tab.id === "linkedin") && !result.facebookPost) ||
                (tab.id === "cold-outreach" && !result.coldOutreachEmail) ||
                (tab.id === "tools" && (result.toolPosts?.length ?? 0) === 0) ||
                (tab.id === "image-prompts" && (result.imagePrompts?.sections?.length ?? 0) === 0);
              const hint =
                tab.id === "image-prompts" && (result.imagePrompts?.sections?.length ?? 0) === 0
                  ? "Run Step 7 (Generate prompts) first"
                  : tab.id === "tools" && (result.toolPosts?.length ?? 0) === 0
                  ? "Run Step 6 (Generate tools) first"
                  : tab.id === "cold-outreach" && !result.coldOutreachEmail
                  ? "Run Step 5 (Generate email) first"
                  : tab.id === "blog" && !result.blog
                    ? "Run Step 3 (Generate blog) first"
                    : (tab.id === "facebook" || tab.id === "linkedin") && !result.facebookPost
                      ? "Run Step 4 (Generate social) first"
                      : undefined;

              return (
                <button
                  key={tab.id}
                  type="button"
                  title={hint}
                  onClick={() => setActiveTab(tab.id)}
                  className={`rounded-t-md px-4 py-2 text-sm font-medium transition-colors ${
                    activeTab === tab.id
                      ? "border-b-2 border-brand text-brand"
                      : unavailable
                        ? "text-muted/70 hover:text-muted"
                        : "text-muted hover:text-foreground"
                  }`}
                >
                  {tab.label}
                </button>
              );
            })}
          </div>

          <div className="pt-5">
            {activeTab === "article" && result.article && (
              <ArticleView
                title={result.article.title}
                metaDescription={result.article.metaDescription}
                bodyHtml={result.article.bodyHtml}
                url={result.articleUrl ?? ""}
                jsonLd={result.articleJsonLd ?? ""}
                keywords={result.article.keywords}
                wordCount={result.article.wordCount}
                sectionOutline={result.article.sectionOutline}
                planOnly={!hasPillarBody}
                targetLabel={`Target: ${CONTENT_LENGTH_TARGETS.pillar.label} words`}
                minWords={CONTENT_LENGTH_TARGETS.pillar.min}
              />
            )}
            {activeTab === "blog" &&
              (result.blog && result.blogUrl ? (
                <ArticleView
                  title={result.blog.title}
                  metaDescription={result.blog.metaDescription}
                  bodyHtml={result.blog.bodyHtml}
                  url={result.blogUrl}
                  jsonLd={result.blogJsonLd ?? ""}
                  keywords={result.blog.keywords}
                  wordCount={result.blog.wordCount}
                  sectionOutline={result.blog.sectionOutline}
                  targetLabel={`Target: ${CONTENT_LENGTH_TARGETS.blog.label} words`}
                  minWords={CONTENT_LENGTH_TARGETS.blog.min}
                />
              ) : (
                <EmptyTabHint message="Run Step 3 to generate the blog post." />
              ))}
            {activeTab === "facebook" &&
              (result.facebookPost ? (
                <SocialView text={result.facebookPost.text} platform="Facebook" />
              ) : (
                <EmptyTabHint message="Run Step 4 to generate social posts." />
              ))}
            {activeTab === "linkedin" &&
              (result.linkedInPost ? (
                <SocialView text={result.linkedInPost.text} platform="LinkedIn" />
              ) : (
                <EmptyTabHint message="Run Step 4 to generate social posts." />
              ))}
            {activeTab === "cold-outreach" &&
              (result.coldOutreachEmail ? (
                <ColdOutreachView email={result.coldOutreachEmail} />
              ) : (
                <EmptyTabHint message="Run Step 5 (Generate email) to create the cold outreach email." />
              ))}
            {activeTab === "tools" &&
              ((result.toolPosts?.length ?? 0) > 0 ? (
                <ToolPostsView tools={result.toolPosts!} />
              ) : (
                <EmptyTabHint message="Run Step 6 to generate tool documents." />
              ))}
            {activeTab === "image-prompts" &&
              (result.imagePrompts ? (
                <ImagePromptsView prompts={result.imagePrompts} />
              ) : (
                <EmptyTabHint message="Run Step 7 to generate Leonardo image prompts." />
              ))}
          </div>
        </div>
      )}
    </div>
  );
}

function EmptyTabHint({ message }: { message: string }) {
  return <p className="rounded-lg border border-dashed border-border bg-background p-4 text-sm text-muted">{message}</p>;
}

function StepRow({
  step,
  title,
  description,
  done,
  disabled,
  isRunning,
  buttonLabel,
  onClick,
  lockedMessage,
}: {
  step: number;
  title: string;
  description: string;
  done: boolean;
  disabled: boolean;
  isRunning: boolean;
  buttonLabel: string;
  onClick: () => void;
  lockedMessage?: string;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-background p-4">
      <div>
        <div className="flex items-center gap-2">
          <span className="flex h-6 w-6 items-center justify-center rounded-full bg-brand/10 text-xs font-bold text-brand">
            {step}
          </span>
          <h3 className="text-sm font-semibold text-foreground">{title}</h3>
          {done && (
            <span className="rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">Done</span>
          )}
        </div>
        <p className="mt-1 text-xs text-muted">{description}</p>
        {lockedMessage && <p className="mt-1 text-xs text-muted">{lockedMessage}</p>}
      </div>
      <button
        onClick={onClick}
        disabled={disabled}
        className="shrink-0 rounded-md bg-brand px-3 py-2 text-sm font-semibold text-white transition-colors hover:bg-brand-dark disabled:opacity-60"
      >
        {isRunning ? "Generating..." : buttonLabel}
      </button>
    </div>
  );
}

function ArticleView({
  title,
  metaDescription,
  bodyHtml,
  url,
  jsonLd,
  keywords,
  wordCount,
  sectionOutline,
  planOnly = false,
  targetLabel,
  minWords,
}: {
  title: string;
  metaDescription: string;
  bodyHtml: string;
  url: string;
  jsonLd: string;
  keywords: string[];
  wordCount: number;
  sectionOutline?: string[];
  planOnly?: boolean;
  targetLabel?: string;
  minWords?: number;
}) {
  const [showSchema, setShowSchema] = useState(false);
  const underTarget = minWords != null && wordCount > 0 && wordCount < minWords;

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-xl font-bold text-foreground">{title}</h3>
        <div className="flex flex-wrap items-center gap-2">
          {wordCount > 0 && (
            <span
              className={`rounded-full px-2.5 py-1 text-xs font-medium ${
                underTarget ? "bg-amber-100 text-amber-800" : "bg-brand/10 text-brand"
              }`}
            >
              {wordCount} words
            </span>
          )}
          {targetLabel && <span className="text-xs text-muted">{targetLabel}</span>}
        </div>
        {planOnly && (
          <span className="rounded-full bg-amber-100 px-2.5 py-1 text-xs font-medium text-amber-800">Plan only</span>
        )}
      </div>
      <p className="mt-1 text-sm text-muted">{metaDescription}</p>
      {url && (
        <a href={url} className="mt-1 inline-block text-sm text-brand hover:underline" target="_blank">
          {url}
        </a>
      )}

      <div className="mt-2 flex flex-wrap gap-1.5">
        {keywords.map((kw) => (
          <span key={kw} className="rounded-full bg-background px-2 py-0.5 text-xs text-muted">
            {kw}
          </span>
        ))}
      </div>

      {planOnly && sectionOutline && sectionOutline.length > 0 && (
        <div className="mt-4 rounded-lg border border-border bg-background p-4">
          <h4 className="text-sm font-semibold text-foreground">Section outline</h4>
          <ol className="mt-2 list-decimal space-y-1 pl-5 text-sm text-muted">
            {sectionOutline.map((heading) => (
              <li key={heading}>{heading}</li>
            ))}
          </ol>
          <p className="mt-3 text-xs text-muted">Run Step 2 to write the full article from this outline.</p>
        </div>
      )}

      {bodyHtml && (
        <div
          className="rendered-content mt-5 rounded-lg border border-border bg-background p-4"
          dangerouslySetInnerHTML={{ __html: bodyHtml }}
        />
      )}

      {jsonLd && (
        <>
          <button
            onClick={() => setShowSchema((v) => !v)}
            className="mt-4 text-sm font-medium text-brand hover:underline"
          >
            {showSchema ? "Hide" : "Show"} JSON+LD Schema
          </button>
          {showSchema && (
            <pre className="mt-2 max-h-96 overflow-auto rounded-lg bg-slate-900 p-4 text-xs text-slate-100">
              {jsonLd}
            </pre>
          )}
        </>
      )}
    </div>
  );
}

function countWords(text: string): number {
  return text.trim().split(/\s+/).filter(Boolean).length;
}

function SocialView({ text, platform }: { text: string; platform: "Facebook" | "LinkedIn" }) {
  const words = countWords(text);
  const chars = text.length;
  const target =
    platform === "Facebook" ? "~40 words, under 250 chars" : "200–300 words, 1,300–1,900 chars";

  return (
    <div>
      <div className="mb-2 flex flex-wrap gap-2 text-xs text-muted">
        <span className="rounded-full bg-brand/10 px-2 py-0.5 font-medium text-brand">{words} words</span>
        <span className="rounded-full bg-brand/10 px-2 py-0.5 font-medium text-brand">{chars} characters</span>
        <span>Target: {target}</span>
      </div>
      <div className="whitespace-pre-wrap rounded-lg border border-border bg-background p-4 text-sm text-foreground">
        {text}
      </div>
    </div>
  );
}

function ColdOutreachView({ email }: { email: ColdOutreachEmailDraft }) {
  const words = countWords(email.bodyText);
  const outOfRange =
    words < CONTENT_LENGTH_TARGETS.emailColdOutreach.min ||
    words > CONTENT_LENGTH_TARGETS.emailColdOutreach.max;

  return (
    <div>
      <div className="mb-2 flex flex-wrap gap-2 text-xs text-muted">
        <span
          className={`rounded-full px-2 py-0.5 font-medium ${
            outOfRange ? "bg-amber-100 text-amber-800" : "bg-brand/10 text-brand"
          }`}
        >
          {words} words
        </span>
        <span>Target: {CONTENT_LENGTH_TARGETS.emailColdOutreach.label} words</span>
      </div>
      <h3 className="text-lg font-semibold text-foreground">{email.subject}</h3>
      <div className="mt-3 whitespace-pre-wrap rounded-lg border border-border bg-background p-4 text-sm text-foreground">
        {email.bodyText}
      </div>
      <p className="mt-3 text-sm text-foreground">
        <span className="font-medium">{email.ctaLabel}: </span>
        <a href={email.ctaUrl} className="text-brand hover:underline" target="_blank" rel="noreferrer">
          {email.ctaUrl}
        </a>
      </p>
    </div>
  );
}

function formatLeonardoSettings(item: ImagePromptSection): string {
  const lines = [
    `Model: ${item.leonardoModel}`,
    `Model ID: ${item.leonardoModelId}`,
    `Dimensions: ${item.width} × ${item.height}`,
    `Style preset: ${item.stylePreset}`,
    `Alchemy: ${item.alchemy ? "On" : "Off"}`,
    `PhotoReal: ${item.photoReal ? "On" : "Off"}`,
  ];
  if (item.notes) lines.push(`Notes: ${item.notes}`);
  return lines.join("\n");
}

function formatLeonardoCopyBlock(item: ImagePromptSection): string {
  return `${formatLeonardoSettings(item)}\n\nPrompt:\n${item.prompt}`;
}

async function copyText(text: string): Promise<void> {
  await navigator.clipboard.writeText(text);
}

function ToolPostsView({ tools }: { tools: ToolPostDraft[] }) {
  return (
    <div className="space-y-4">
      {tools.map((tool) => (
        <ToolPostCard key={tool.slug} tool={tool} />
      ))}
    </div>
  );
}

function ToolPostCard({ tool }: { tool: ToolPostDraft }) {
  const [showSchema, setShowSchema] = useState(false);

  return (
    <div className="rounded-lg border border-border bg-background p-4">
      <h3 className="text-lg font-semibold text-foreground">{tool.title}</h3>
      <p className="mt-1 text-sm text-muted">{tool.metaDescription}</p>
      <a href={tool.toolUrl} className="mt-1 inline-block text-sm text-brand hover:underline" target="_blank">
        {tool.toolUrl}
      </a>

      <div
        className="rendered-content mt-4 rounded-lg border border-border bg-surface p-4"
        dangerouslySetInnerHTML={{ __html: tool.bodyHtml }}
      />

      {tool.jsonLdSchema && (
        <>
          <button
            onClick={() => setShowSchema((v) => !v)}
            className="mt-4 text-sm font-medium text-brand hover:underline"
          >
            {showSchema ? "Hide" : "Show"} JSON+LD Schema
          </button>
          {showSchema && (
            <pre className="mt-2 max-h-96 overflow-auto rounded-lg bg-slate-900 p-4 text-xs text-slate-100">
              {tool.jsonLdSchema}
            </pre>
          )}
        </>
      )}
    </div>
  );
}

function ImagePromptsView({ prompts }: { prompts: ImagePromptsSet }) {
  const pillarSections = prompts.sections.filter((s) => s.sourceType === "pillar");
  const blogSections = prompts.sections.filter((s) => s.sourceType === "blog");

  return (
    <div className="space-y-8">
      <p className="text-sm text-muted">
        Copy a prompt and Leonardo settings into{" "}
        <a
          href="https://app.leonardo.ai/"
          className="text-brand hover:underline"
          target="_blank"
          rel="noreferrer"
        >
          Leonardo.ai
        </a>
        . One image per H2 section — prompts only, no images generated here.
      </p>
      {pillarSections.length > 0 && (
        <ImagePromptSectionGroup title="Pillar sections" sections={pillarSections} />
      )}
      {blogSections.length > 0 && (
        <ImagePromptSectionGroup title="Blog sections" sections={blogSections} />
      )}
    </div>
  );
}

function ImagePromptSectionGroup({
  title,
  sections,
}: {
  title: string;
  sections: ImagePromptSection[];
}) {
  return (
    <div className="space-y-4">
      <h3 className="text-base font-semibold text-foreground">{title}</h3>
      {sections.map((item) => (
        <ImagePromptCard key={`${item.sourceType}-${item.order}-${item.heading}`} item={item} />
      ))}
    </div>
  );
}

function ImagePromptCard({ item }: { item: ImagePromptSection }) {
  const [copied, setCopied] = useState<"prompt" | "all" | null>(null);

  async function handleCopy(mode: "prompt" | "all") {
    await copyText(mode === "prompt" ? item.prompt : formatLeonardoCopyBlock(item));
    setCopied(mode);
    window.setTimeout(() => setCopied(null), 2000);
  }

  return (
    <div className="rounded-lg border border-border bg-background p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <h3 className="text-base font-semibold text-foreground">
          {item.order}. {item.heading}
        </h3>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => handleCopy("prompt")}
            className="rounded-md border border-border px-3 py-1.5 text-xs font-medium text-foreground hover:bg-surface"
          >
            {copied === "prompt" ? "Copied!" : "Copy prompt"}
          </button>
          <button
            type="button"
            onClick={() => handleCopy("all")}
            className="rounded-md bg-brand px-3 py-1.5 text-xs font-medium text-white hover:opacity-90"
          >
            {copied === "all" ? "Copied!" : "Copy prompt + settings"}
          </button>
        </div>
      </div>
      <pre className="mt-3 whitespace-pre-wrap rounded-md border border-border bg-surface p-3 text-xs text-muted">
        {formatLeonardoSettings(item)}
      </pre>
      <div className="mt-3 whitespace-pre-wrap rounded-md border border-border p-3 text-sm text-foreground">
        {item.prompt}
      </div>
    </div>
  );
}

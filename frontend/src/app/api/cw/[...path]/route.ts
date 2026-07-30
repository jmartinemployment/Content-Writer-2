import { NextRequest } from "next/server";

/**
 * Server-side proxy to GeekAPI for content-writer-v2's merged routes. GEEK_BACKEND_API_KEY
 * authenticates this proxy to GeekAPI (via its X-API-Key fallback auth) and must never reach the
 * browser — see GeekAPI/CLAUDE.md "Known anti-pattern" on client-side token exposure.
 */
const GEEK_API_URL = process.env.CONTENT_WRITER_GEEK_API_URL ?? "http://localhost:8080";
const GEEK_BACKEND_API_KEY = process.env.GEEK_BACKEND_API_KEY ?? "";

async function proxy(request: NextRequest, path: string[]): Promise<Response> {
  // path already starts with "api" — callers build URLs as `${API_BASE_URL}/api/...`, and
  // API_BASE_URL is "/api/cw", so a call to "/api/clients" arrives here as ["api", "clients"].
  const targetUrl = new URL(`/${path.join("/")}`, GEEK_API_URL);
  targetUrl.search = request.nextUrl.search;

  const headers = new Headers(request.headers);
  headers.delete("host");
  headers.delete("connection");
  headers.set("X-API-Key", GEEK_BACKEND_API_KEY);

  const hasBody = request.method !== "GET" && request.method !== "HEAD";
  const response = await fetch(targetUrl, {
    method: request.method,
    headers,
    body: hasBody ? request.body : undefined,
    duplex: hasBody ? "half" : undefined,
    redirect: "manual",
  } as RequestInit);

  const responseHeaders = new Headers(response.headers);
  responseHeaders.delete("content-encoding");
  responseHeaders.delete("content-length");

  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers: responseHeaders,
  });
}

async function handler(request: NextRequest, ctx: RouteContext<"/api/cw/[...path]">): Promise<Response> {
  const { path } = await ctx.params;
  return proxy(request, path);
}

export const GET = handler;
export const POST = handler;
export const PUT = handler;
export const PATCH = handler;
export const DELETE = handler;

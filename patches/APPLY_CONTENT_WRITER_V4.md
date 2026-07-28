# Apply Content Writer v4 to GeekBackend

This cloud agent workspace is Content-Writer-2 and cannot push to `jmartinemployment/GeekBackend`.
The full V4 backend commit lives locally on branch `cursor/content-writer-v4-7612` in the cloned GeekBackend, and as a patch here.

## Apply the patch

```bash
cd /path/to/GeekBackend
git checkout -b cursor/content-writer-v4-7612
git apply /path/to/Content-Writer-2/patches/content-writer-v4-geekbackend.patch
# or: git am < patches/content-writer-v4-geekbackend.patch
```

## Frontend

The Next.js app is in `content-writer-v4/` in this repo (also intended as its own GitHub repo).

```bash
cd content-writer-v4
cp .env.example .env.local
npm install
npm run dev
```

Requires GeekRepository + GeekAPI running with `DATABASE_URL`, `OPENAI_API_KEY`, and/or `ANTHROPIC_API_KEY`.

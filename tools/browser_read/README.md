# MVP-BROWSER-READ-01 synthetic browser test

This is a test runner, not a development authentication server. Run from the repository root. Requires restored .NET 10 dependencies, Docker with `postgres:17.6-alpine`, Node 24/pnpm, installed frontend dependencies, Playwright and Chrome/Chromium.

```sh
pnpm --dir src/Lms.Web build
pnpm --dir src/Lms.Web exec vite build --config vite.browser-read.config.ts
export LMS_BROWSER_READ=1
# Optional when node, playwright and Chrome are not in their normal locations:
export LMS_BROWSER_NODE=/absolute/path/to/node
export LMS_PLAYWRIGHT_MODULE=/absolute/path/to/playwright/index.mjs
export LMS_BROWSER_EXECUTABLE=/absolute/path/to/chrome

dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Browser_read' --logger trx --results-directory /tmp/browser-read/e2e
pnpm --dir src/Lms.Web test
dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/browser-read/regression
```

Without `LMS_BROWSER_READ=1`, the two browser tests explicitly SKIP, not PASS. With it, missing build/browser/dependencies fail the test. Test UI build output is ignored by Git. It is separate from the normal `index.html` production entry. Production Program.cs and production frontend activation remain unchanged.

Each xUnit case creates a disposable PostgreSQL database/container, real S2 user/session/grants and an immutable synthetic publication. Kestrel binds `127.0.0.1:0` and is disposed with the test. Request contexts use the restricted runtime login and real S2/S3/current-processing/read-only adapters. Existing synthetic clock implementation stays in the test assembly. Processing mutations use the accepted adapter with expected revision; no processing guard bypass.

The real issuer's JWT travels from C# to the Node child through an anonymous stdin pipe, then through Playwright's in-memory init script to the existing `window.lmsAuth` contract. The browser sends normal Bearer authentication, validated by ComponentJwt. There are no fixture/auth/control HTTP endpoints, custom actor headers, token URLs, cookies, localStorage/sessionStorage, or public token files. Control commands go back over the child stdout pipe and are not accessible to pages. The host clears logging providers; it does not log credentials, bodies, database connection strings or request headers. Browser diagnostics are scanned for credential/private markers. No HAR, trace, video or persistent browser profile is saved. xUnit diagnostics redact JWT-shaped strings on failure.

Playwright drives real clicks, history, browser fetch and offline mode; it never routes/fulfills/mocks API requests. Tests run once for Student and once for Teacher. Screenshots and secret-free results go to `codex/evidence/MVP-BROWSER-READ-01/{Student,Teacher}/`. They intentionally contain synthetic displayed course content. They do not contain issued tokens or fixture identity values.

The tests require API `Cache-Control: no-store`, default-cache priming before restriction, a fresh denied response after restriction, no API memory/disk/prefetch/SW cache signals or 304, empty web storage/CacheStorage/IndexedDB/cookies, and actual re-fetch on history navigation. Browser caching is not globally disabled. This is Chromium-specific evidence, not a promise to erase already rendered content, screenshots, memory or disk sectors. It does not prove deployment timing/G02/CLOCK-DEPLOY-01 or authorize production use.

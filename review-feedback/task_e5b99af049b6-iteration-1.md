# Review — task_e5b99af049b6, iteration 1

**Verdict: REWORK**

**Task under review:** Task 5 of 6 — `ARCHITECTURE_REVIEW_P0.md`, section
`## 6. Небезопасная генерация кодов и паролей + отсутствие ограничений на подбор`
(prescribed fix in that section: «`RandomNumberGenerator.GetInt32` / `GetBytes`; код 6+ цифр;
`ExpiresAt` (5–10 мин) + `AttemptCount` с блокировкой; `AddRateLimiter` на `/Users/CreateAuthRequest`
и `/connect/token`; `CryptographicOperations.FixedTimeEquals`»).

Most of this attempt is correct and I want that on the record before the blocking points: the
cryptographic RNG, the 6-digit code, the expiry/attempt counter, the migration, the constant-time
comparison and the single-use consumption are all implemented properly and verified below
(see "What I verified as correct"). The rework is confined to acceptance criterion 5 —
rate limiting — where the delivered control does not do what it claims in this deployment and,
as shipped, is a net availability regression.

---

## Blocking point 1 — the IP partition collapses into ONE partition for the whole internet, so AC5 is not met and the change introduces a new denial-of-service

**Where:** `src/backend/Core/Shared/Shared.WebApi/RateLimiting/RateLimitingExtensions.cs:115-126`,
specifically line 123:

```csharp
var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
```

used as the partition key for both `AuthRequestPolicy` (line 42) and `TokenPolicy` (line 45).

**What is actually true in this deployment — evidence, not inference:**

1. `deploy/docker-compose.infra.yml:10-35` — Traefik v3.6.2 runs as the edge proxy on the
   `infra_traefik` network, owning entrypoints `web:80` and `websecure:443`.
2. `deploy/docker-compose.app.yml:7-31` (`gateway`) and `:72-96` (`identity-webapi`) — both services
   are attached to `infra_traefik` and declare **no `ports:` at all**. They are reachable only through
   the Traefik routers `api.${LOCAL_DOMAIN}` / `auth.${LOCAL_DOMAIN}`.
3. `deploy/docker-compose.infra.local.yml:13-26` — on the local stand the **only** published ports on
   the whole stack are Traefik's (`8181`, `8443`). `deploy/docker-compose.app.stage.yml:17-31,80-93` —
   on stage the same, behind `api.${STAGE_DOMAIN}` / `auth.${STAGE_DOMAIN}`, ports 80/443 on Traefik only.
4. `grep -rn "ForwardedHeaders\|KnownProxies\|KnownNetworks\|X-Forwarded" src/ deploy/` returns **only
   your own comment** at `RateLimitingExtensions.cs:120-122`. There is no `UseForwardedHeaders`, no
   `ForwardedHeadersOptions`, and no `ASPNETCORE_FORWARDEDHEADERS_ENABLED` anywhere in the repository.

**Therefore:** for every external caller, in both the local docker stand and on stage,
`HttpContext.Connection.RemoteIpAddress` inside Gateway.WebApi and Identity.WebApi is the **Traefik
task's address on the `infra_traefik` overlay network** — one constant value for all callers on Earth.
(The `switch-gateway-mode.sh local` variant is no better: Traefik then proxies to
`host.docker.internal:5000`, so the observed address is the Docker host bridge address — again one
constant.) Your own comment at lines 120-122 states the precondition — "A reverse-proxy deployment has
to enable ForwardedHeaders with an explicit KnownProxies list for the real client IP to arrive here" —
and this repository *is* that deployment, and the precondition was not satisfied.

**Why this blocks:**

* **AC5 is not satisfied.** AC5 requires "partition per client IP and/or per email". A partition keyed
  on the proxy's address is not a partition per client; it is a single global bucket. For
  `/Users/CreateAuthRequest` there is at least a separate per-email partition (but see blocking point 3);
  for `/connect/token` there is nothing else at all (blocking point 2).
* **It is a new P0-class availability vulnerability, not merely an ineffective control.**
  `Gateway.WebApi/appsettings.json` sets `AuthRequest` to 10 permits / 300 s and
  `Identity.WebApi/appsettings.json` to `Token` 20 permits / 300 s. Collapsed into one partition these
  become: *the entire product accepts 10 auth-request creations per 5 minutes and 20 token exchanges
  per 5 minutes, globally.* Normal traffic alone will start returning 429 to legitimate users, and a
  single attacker spending 20 cheap requests keeps every user of the system locked out of
  authentication for the rest of every window. Converting a brute-force (confidentiality) risk into a
  guaranteed, trivially-triggered outage (availability) is not an acceptable trade — a rate-limit
  partition that any one caller can exhaust on behalf of everyone is the specific anti-pattern this
  control must avoid.

**What must be done (either route is acceptable):**

*Preferred — make the real client IP arrive at the limiter.* In **both** `Gateway.WebApi/Program.cs`
and `Identity.WebApi/Program.cs`, register `UseForwardedHeaders` as the **first** middleware in the
pipeline (before `UseCors` / `UseAuthRequestEmailPartitionKey` / `UseRateLimiter`), configured with:

* `ForwardedHeaders = ForwardedHeaders.XForwardedFor` (add `XForwardedProto` if you also want correct
  scheme detection),
* `ForwardLimit = 1`,
* an explicit `KnownProxies` / `KnownNetworks` entry covering the Traefik task address or the
  `infra_traefik` overlay subnet — **this is mandatory**: the default `KnownNetworks`/`KnownProxies`
  contain only loopback, so without it `ForwardedHeadersMiddleware` silently does nothing and you get
  exactly today's behaviour with no error anywhere.

Make the proxy/network list configurable through appsettings (it differs between local and stage) and
document in `deploy/` that Traefik must be the only writer of `X-Forwarded-For` for the endpoints in
question. Once `RemoteIpAddress` is the true client, the existing `ByClientIp` code becomes correct as
written and both endpoints get a genuine per-client partition.

*Acceptable alternative if you do not want to touch the proxy trust chain in this task:* remove the
IP-keyed fixed window as delivered (do not ship a global cap disguised as a per-client limit) and
carry AC5 entirely on per-caller identifiers — see blocking points 2 and 3, which must be fixed either
way. Do **not** simply raise the permit limits: that does not make the partition per-client, it only
moves the outage threshold.

---

## Blocking point 2 — `/connect/token` has no per-caller partition of any kind

**Where:** `RateLimitingExtensions.cs:44-45` (`TokenPolicy` → `ByClientIp` only) and
`RateLimitingExtensions.cs:49-50` — the `GlobalLimiter` is the e-mail partition, and `ByEmail`
(lines 128-136) returns `RateLimitPartition.GetNoLimiter` unless
`context.Items[EmailPartitionKeyItem]` was set. That item is set exclusively by
`UseAuthRequestEmailPartitionKey`, which is registered only in `Gateway.WebApi/Program.cs:106` and
only for the path `/Users/CreateAuthRequest`. Identity.WebApi never registers it
(`Identity.WebApi/Program.cs:62` calls `UseRateLimiter` with no key-capture middleware).

**Why this blocks:** `/connect/token` is *the* endpoint on which the verification code is actually
guessed — `VerifyAuthToTokenMiddleware` intercepts `POST /connect/token` carrying `id` +
`verificationCode` and calls `VerifyAuthRequest` from there. AC5 names this endpoint explicitly and
requires it to be partitioned "per client IP and/or per email". As delivered it is partitioned by
neither: with blocking point 1 in force it is a single global bucket of 20 requests / 5 minutes
shared by every login, every verification exchange and every refresh-token renewal in the product.
That fails AC5 on its own terms, independently of the DoS argument.

**What must be done:** give the token endpoint a real per-caller partition. Fixing blocking point 1
(real client IP) satisfies this and is the smallest change. If you additionally want a second
dimension analogous to the e-mail one, capture the `username` (password grant) and/or `id`
(verification exchange) form field in a small middleware registered *before* `UseRateLimiter` in
`Identity.WebApi/Program.cs` — note that a partition-key callback is synchronous and cannot read the
form itself, which is why `UseAuthRequestEmailPartitionKey` exists in the first place.

---

## Blocking point 3 — the per-e-mail partition is opt-out for the caller: omit `Content-Length` and it disappears

**Where:** `RateLimitingExtensions.cs:72-75`:

```csharp
if (HttpMethods.IsPost(request.Method) &&
    request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase) &&
    request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true &&
    request.ContentLength is > 0 and <= maximumInspectedBodyBytes)
```

**Why this blocks:** Kestrel leaves `HttpRequest.ContentLength` **null** for a request sent with
`Transfer-Encoding: chunked` (and for HTTP/2 requests that do not declare a length). `null is > 0`
is `false`, so the whole block is skipped, `EmailPartitionKeyItem` is never set, `ByEmail` returns
`GetNoLimiter`, and the per-e-mail limit does not apply — while MVC's JSON input formatter binds the
chunked body perfectly happily, so the request still succeeds. An attacker flooding one mailbox with
verification codes only has to drop one header. Combined with blocking point 1, the *effective*
per-client protection on `/Users/CreateAuthRequest` is currently zero.

This matters especially because the per-e-mail partition is the one arm of AC5's "and/or" that could
otherwise have carried the requirement for this endpoint on its own. It cannot, as written.

**What must be done:** make e-mail capture unconditional for the target path, with an explicit and
enforced read bound rather than a trusted `Content-Length`. Either:

* read the body when `ContentLength` is null too, bounding the read yourself (e.g. copy at most
  `maximumInspectedBodyBytes` into a pooled buffer / `MemoryStream` and abandon key extraction if the
  body is longer, then rewind), or
* reject (`400`/`411`) auth-request calls to that path that do not declare a `Content-Length` inside
  the accepted range, so the limiter's precondition is enforced rather than assumed.

Keep the existing hard cap — do not remove it. Note the current cap *is* correct as a DoS guard and I
verified it (below); the problem is only that a caller can step around the whole path.

---

## What I verified as correct (do not change these)

* **AC1 — no `System.Random` on any security path.** `grep -rn "new Random(" src/` → no matches.
  `grep -rn --include="*.cs" "Random\." src/ | grep -v RandomNumberGenerator` → no matches. The only
  remaining textual hit for "Random" outside `RandomNumberGenerator` is the doc-comment cross-reference
  at `VerificationCodeGenerator.cs:8`. Identity and Sample are clean too.
* **AC2 — 6+ digits, no modulo bias.** `Users.Application/Security/VerificationCodeGenerator.cs:17-21`
  draws each digit with `RandomNumberGenerator.GetInt32(10)`, which is rejection-sampled and unbiased
  by contract — there is no `%` and no `GetBytes`-then-reduce anywhere. Length floor enforced at
  `AuthRequestOptions.cs:24-28` (`Math.Max(6, value)`), configured to 6 in
  `Users.Service/appsettings.json`. `TemporaryPasswordGenerator.cs` likewise uses `GetInt32` per
  character and `RandomNumberGenerator.Shuffle` (line 34) — the old biased `Random`-based
  Fisher-Yates is gone.
* **AC3 — expiry + attempt counter + migration.** `AuthRequest.cs` gains `CreatedAt`, `ExpiresAt`,
  `AttemptCount` and the `IsExpired`/`IsOutOfAttempts`/`RegisterFailedAttempt` helpers. Migration
  `20260906080718_AuthRequestExpiryAndAttempts.cs` matches the style of `20260308111540_Initial.cs`
  (BOM, `#nullable disable`, `<inheritdoc/>`, Up+Down); the `.Designer.cs` and
  `UserDbContextModelSnapshot.cs` were both updated consistently. I ran
  `dotnet ef migrations has-pending-model-changes --project Users.Infrastructure --startup-project
  Users.Service` → **"No changes have been made to the model since the last migration."**
  I also generated the SQL (`dotnet ef migrations script 20260308115353_Username
  20260906080718_AuthRequestExpiryAndAttempts`): three `ALTER TABLE ... ADD` statements,
  `DEFAULT TIMESTAMPTZ '-infinity'` for both timestamps. That default is **correct and safe** on a
  non-empty table: pre-existing rows are born expired, so old codes stop being accepted — failing
  closed, which is the right direction, and Npgsql reads `-infinity` back as `DateTimeOffset.MinValue`
  without throwing. No scratch migration was left behind.
* **AC4 — verification control flow.** Read line by line in `VerifyAuthRequestHandler.cs`. Unknown id,
  expired and wrong-code all answer with the same message (good — no oracle). Wrong code:
  `RegisterFailedAttempt()` runs *first*, then either the request is deleted at the limit or the
  incremented counter is persisted via `UpdateAsync` + `SaveChangesAsync` before the throw — there is
  **no early return that skips the increment**. I confirmed the increment actually persists:
  `AbstractRepository.AsQueryable()` (`Shared.Infrastructure/Repository/AbstractRepository.cs:22-25`)
  returns a **tracked** query (no `AsNoTracking`), and `UpdateAsync` → `UpdateRange` marks the entity
  Modified. `FixedTimeEquals` is applied to two 32-byte SHA-256 buffers, so length differences leak
  nothing. With `MaxVerificationAttempts = 5`, guesses 1-4 are counted and rejected and guess 5
  destroys the request.
* **AC6 — replay prevention.** `ConsumeAsync` (delete + `SaveChangesAsync`) runs immediately after the
  code matches and **before** `ProcessNewUser` / `ProcessExistUser` issue any credential. A code
  cannot be replayed even if a later step throws.
* **AC7 — build.** `dotnet build src/backend/Core/Core.sln` and
  `dotnet build src/backend/Core/Core.sln --no-incremental`: **Build succeeded, 0 Errors**, 2 warnings
  (pre-existing `NU1903` for AutoMapper 15.0.1, unrelated to this change).
* **Options clamps only tighten.** `AuthRequestOptions.cs`: code length `Math.Max(6, v)`; lifetime
  `Math.Clamp(v, 5, 10)`; attempts `Math.Max(1, v)`; temporary password `Math.Max(20, v)`. The binder
  goes through the setters, and omitted keys fall back to field initialisers that are already inside
  the floor. Section path `Authentication:AuthRequest` matches `Users.Service/appsettings.json`, and
  `AddUsersApplication` (the only registration site, `Users.Service/Startup.cs:17`) configures it and
  registers `TimeProvider.System`.
* **Middleware order does apply the endpoint policies.** Neither Program.cs calls `UseRouting`
  explicitly, so `WebApplication` inserts routing ahead of all user middleware and `UseEndpoints`
  after it; `UseRateLimiter` therefore sees the resolved endpoint and its `[EnableRateLimiting]`
  metadata. `/Users/CreateAuthRequest` (`[Route("[controller]")]` + `[HttpPost(nameof(...))]`) and
  `/connect/token` (`[Route("connect")]` + `[HttpPost("token")]`) both carry the attribute. In
  Identity the limiter at `Program.cs:62` genuinely runs before `VerifyAuthToTokenMiddleware`
  (`:66`), and critically `VerifyAuthToTokenMiddleware` does **not** rewrite the path — it only
  swaps the form body on the same `/connect/token` request — so a verification guess is counted.
  *Honest caveat: I established this by reading the pipeline and the documented `WebApplication`
  behaviour, not by executing the apps; I did not stand up a running host.*
* **Body-buffering middleware — no unbounded-buffering DoS.** The `ContentLength <= 4096` gate means
  the middleware never buffers more than 4 KiB, and Kestrel enforces the declared length, so there is
  no way to make the gateway buffer a large body ahead of the limiter. `EnableBuffering()` stays under
  the 30 KiB memory threshold, so no temp file is spilled. Model binding downstream is unaffected:
  `request.Body.Position = 0` is executed outside the `try`, so it runs on the malformed path too, and
  the buffered stream is seekable. Malformed JSON, a non-object root, a non-string or blank `email`
  are all handled without throwing. The path guard is correct for `/Users/CreateAuthRequest`.
  (The one hole is the *skip* path — blocking point 3 — not the buffering itself.)
* **§1 not regressed.** `CreateAuthRequestHandler` still calls
  `verificationCodeSender.SendAsync(...)` (`:45-46`) and still maps to a DTO that carries no code;
  `IVerificationCodeSender` wiring in `Users.Infrastructure/Extensions.cs:39` is untouched.
* **§7 not silently done.** The temporary password still flows through `UserDto.Password` and the bus
  exactly as before — correctly left for the next task.
* **No harness residue, no stray project.** `git status --porcelain --untracked-files=all` shows only
  the 13 modified files, the 6 new source/migration files, and the four read-only
  `ARCHITECTURE_REVIEW*.md` specs. `src/backend/Core/Core.sln` is unmodified (34 projects). Nothing is
  missing or unexpectedly deleted — I looked specifically, given the earlier denied `rm -rf` prompt,
  and the worktree is intact.

---

## Non-blocking observations (fix if convenient, do NOT gate on these; some belong to other tasks)

1. `AttemptCount` is a read-modify-write with no `rowversion`/concurrency token, so parallel guesses
   against one request can under-count. Your own flagged follow-up; correct to raise, out of scope here.
2. `GetDevelopmentAuthCodeHandler` still returns the code for an expired request. Development-only and
   double-gated, but worth aligning with the new expiry later.
3. `services.Configure<RateLimitingOptions>(section)` at `RateLimitingExtensions.cs:33` is registered
   but never consumed — the policies close over the eagerly bound `options` local at line 35. Harmless,
   but it means `IOptionsMonitor` reload will never affect the limits; either drop the `Configure` call
   or say so in the doc-comment.
4. The e-mail partition key is attacker-controlled and unbounded in cardinality; `PartitionedRateLimiter`
   does evict idle partitions, so this is bounded in practice, but it is worth a length cap on the
   captured key.
5. `UseAuthRequestEmailPartitionKey` uses `StartsWithSegments`, so `/Users/CreateAuthRequest/anything`
   also engages it. Harmless today; `Equals` would be tighter.

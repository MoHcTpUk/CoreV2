# Review — task_a4f045af8722, iteration 1 (post-commit review)

**Verdict: REWORK**

**Under review:** commit `910aacc0852d9760f9455d012dd7847e9b72b9a8`
«fix(architecture): Пароль в открытом виде ходит по шине и лежит в DTO» — `ARCHITECTURE_REVIEW_P0.md`,
section `## 7. Пароль в открытом виде ходит по шине и лежит в DTO`. Parent: `0823a12` (accepted P0 §6).
The commit was made without a review verdict; this is that review. All line numbers below are **at HEAD
(`910aacc`)**.

Most of the commit is correct and was verified against real infrastructure (see "What I verified as
correct — do not change"). The rework is small and confined to two points:

1. the new bearer ticket is written **in plaintext to the Identity.WebApi log on every login**, and I
   demonstrated a logged ticket being redeemed afterwards for a real session;
2. the NATS TLS deployment ships the **private keys** (server key, and on the openssl branch the CA key)
   to every application container, and makes the server key world-readable for a reason that is not true.

---

## Blocking point 1 — the login ticket is logged in plaintext by OpenIddict, and a logged ticket can be replayed

**Where:**

- `src/backend/Core/Services/Identity/Identity.WebApi/Middleware/VerifyAuthToTokenMiddleware.cs:107-113` —
  the rewritten form puts the secret into the OAuth request:
  `[AuthRequestGrant.TicketParameter] = ticket,` (line 110).
- `src/backend/Core/Services/Identity/Identity.WebApi/Controllers/TokenController.cs:79` —
  `var ticket = (string?)request.GetParameter(AuthRequestGrant.TicketParameter);`
- `src/backend/Core/Services/Identity/Identity.WebApi/Security/AuthRequestGrant.cs:19` — `TicketParameter = "ticket"`.
- Log level: `src/backend/Core/Services/Identity/Identity.WebApi/appsettings.json:35-39`
  (`Default: Information`; no Stage override).

**What happens (observed, not inferred).** OpenIddict logs every extracted token request at `Information`
(`OpenIddict.Server.OpenIddictServerDispatcher`). It redacts only the parameters it knows are secrets
(`password`, `refresh_token`, `code`, …). A custom `ticket` parameter is printed verbatim. From the
Identity.WebApi output during an ordinary verification-code login (the request rewritten by the middleware):

```
info: OpenIddict.Server.OpenIddictServerDispatcher[0]
      The token request was successfully extracted: {
        "grant_type": "auth_request",
        "ticket": "c01d38135ca94c469f496ab35a49436d.ljjx82kzu-YBMSVaTFkgtbFm4b4m3DPNrJX9qIgv7MU",
        "scope": "openid profile email offline_access",
        "client_id": "core-app"
      }.
```

In the same log a `refresh_token` request shows `"refresh_token": "[redacted]"`. The old password flow
did not have this leak, because OpenIddict redacts `password`. So the commit adds a new credential-in-logs
exposure (CWE-532) in the very place it was meant to stop credentials leaking.

**Why it is exploitable, not theoretical.** The ticket is logged *before* `AuthTicketService.RedeemAsync`
reads the row (`AuthTicketService.cs:65`) and marks it spent (`AuthTicketService.cs:78`). Any failure between
those two points leaves a live ticket (up to 60 s) sitting in `docker service logs` / Portainer / the log
shipper. I reproduced this: 40 concurrent `auth_request` redemptions of 40 distinct fresh tickets gave
1×200 and 39×400 (`invalid_request`, "A second operation was started on this context instance…", which
comes from the pre-existing singleton DbContext, see non-blocking N2). **All 39 of those tickets were still
unspent**, and replaying each one afterwards returned **HTTP 200 with an access token whose `sub` is the
bound user**. So a person with log access can turn any failed login into a session for that user (with
`offline_access`, a long-lived refresh token). DB errors, a restarted pod or a cancelled request would leave
the same gap.

A secondary effect of the same design (review item B): because the ticket is an ordinary form parameter, an
external caller can also POST `grant_type=auth_request&ticket=…&client_id=core-app` straight to
`/connect/token` and receive tokens (verified: 200). The ticket is never meant to reach a client, and
`OpenApiConfiguration.cs:64,86` says so ("callers never send it themselves"), but nothing enforces it.

**What must be done (use this approach):** keep the ticket out of the OpenIddict request entirely, and pass
it from the middleware to the controller through server-side per-request state.

1. `Identity.WebApi/Security/AuthRequestGrant.cs`: replace `TicketParameter` with an `HttpContext.Items` key,
   e.g. `public const string TicketItemKey = "AuthRequestGrant:Ticket";`. No form parameter name should remain.
2. `Identity.WebApi/Middleware/VerifyAuthToTokenMiddleware.cs`: after `userService.VerifyAuthRequest(...)`
   succeeds (currently lines 86-87), set `context.Items[AuthRequestGrant.TicketItemKey] = verified.Ticket;`.
   Build the rewritten form (lines 107-113) with **only** `grant_type=auth_request`, `scope` and `client_id`,
   with **no ticket**. Set the Items entry only on that verified branch.
3. `Identity.WebApi/Controllers/TokenController.cs` `ExchangeAuthRequestAsync` (lines 76-98): read the
   ticket **only** from `HttpContext.Items[AuthRequestGrant.TicketItemKey]` (as `string`). Never read it from
   `request.GetParameter(...)` or the form. If it is missing or empty, return the same `invalid_grant` Forbid
   that is already there. Never put the ticket into an error description or a log message.
4. Leave `UseTokenSubjectPartitionKey` / rate limiting untouched. It keys on the inbound `id`, which is
   unchanged.
5. Keep the OpenAPI wording (`OpenApiConfiguration.cs:64,86`) accurate: `auth_request` is internal-only and
   cannot be invoked externally.

**Acceptance checks for this point (run them, against real Postgres + NATS):**

- (a) A verification-code login (`POST /connect/token` with `id` + `verificationCode`) returns 200. Identity.WebApi's
  complete console output, at default levels **and** with `Logging__LogLevel__Default=Debug`, contains
  neither the 43-character secret nor the 32-hex id of the ticket that was issued. Get both from the
  `AuthTickets` row or a debugger; grep the log for them.
- (b) An external `POST /connect/token` with `grant_type=auth_request&ticket=<valid, unspent ticket>&client_id=core-app`
  returns `400 invalid_grant`, and that ticket's `RedeemedAt` stays NULL. To test, insert the row directly
  with `SecretHash = base64(SHA256(utf8(secret)))`.
- (c) Unchanged behaviour still holds: a second login for the same account returns 200, and the first session's
  refresh token still works. 13 wrong codes for one `id` give 10×401 then 3×429.

---

## Blocking point 2 — NATS private keys are mounted into every application container; server key made world-readable

**Where:**

- `deploy/docker-compose.app.local.yml:17,26,34,42,50` and `deploy/docker-compose.app.stage.yml:23,48,57,66,83` —
  every app service (gateway, users, sample, identity, identity-webapi) mounts the **whole directory**
  `./certs/nats:/etc/nats/certs:ro`. The comments at `docker-compose.app.local.yml:11` and
  `docker-compose.app.stage.yml:11` say the services need only `ca.pem`.
- `deploy/generate-nats-certs.sh:48,62` — on the openssl branch (the one a stage server without `mkcert`
  takes) the **CA private key** is written into that same directory as `ca-key.pem` and left there. I ran the
  branch: the directory ends up with `ca-key.pem, ca.pem, nats-cert.pem, nats-key.pem`.
- `deploy/generate-nats-certs.sh:83` — `chmod 644 "$CERT_DIR/nats-key.pem"`, justified at line 82 by
  "the nats process inside the container does not run as root".

**Why this is wrong/unsafe:**

- The internet-facing `gateway` container and the four other services receive the NATS **server private key**
  and, on stage, the **CA private key** (valid 10 years), though none of them needs anything but the CA
  certificate. Least privilege is violated in exactly the deliverable AC5 asks for.
- The CA key adds a capability the bus password does not give. Anyone who reads it from any app container can
  mint a `nats` certificate that every service trusts (`NatsTlsCaFile` + `CustomRootTrust`) for a decade.
  **Rotating the NATS password after an incident does not remove that ability**; the CA must be replaced and
  every service redeployed.
- The 0644 justification is false. `nats:latest` has no `USER` (`docker image inspect nats:latest` →
  `User: ""`, entrypoint `/nats-server`, i.e. root). I started the repo's `nats-server.conf` with the
  certificate files at **0600** and the server came up with `TLS required for client connections` /
  `Server is ready`. World-readable key files on the stage host are therefore unnecessary.

**What must be done:**

1. In **both** `deploy/docker-compose.app.local.yml` and `deploy/docker-compose.app.stage.yml`, replace each
   of the five `- ./certs/nats:/etc/nats/certs:ro` lines with a single-file mount of the CA certificate:
   `- ./certs/nats/ca.pem:/etc/nats/certs/ca.pem:ro`. `NatsTlsCaFile=/etc/nats/certs/ca.pem` in the
   appsettings stays valid. Update the comments accordingly.
2. `deploy/generate-nats-certs.sh`, openssl branch: the CA private key must not stay in `deploy/certs/nats/`
   (that directory is mounted into the nats container). Either write it to a separate directory that is never
   mounted, e.g. `deploy/certs/nats-ca/ca-key.pem` with mode 0600 (confirm it is git-ignored with
   `git check-ignore -v deploy/certs/nats-ca/ca-key.pem`), or delete it right after signing the server
   certificate. With deletion a later reissue creates a new CA, so print a note that all services must be
   restarted after a reissue. Pick one and document it in the script header.
3. `deploy/generate-nats-certs.sh:81-83`: `ca.pem` and `nats-cert.pem` may stay 0644. `nats-key.pem` must be
   0600, and the CA key 0600 if kept. Fix the comment on line 82.
4. `deploy/docker-compose.infra.yml:84` (nats) may keep mounting the directory, but after step 2 it must not
   contain a CA key.
5. Keep `deploy/start_stage.sh`'s pre-flight check working (it checks `ca.pem`, `nats-cert.pem`,
   `nats-key.pem` in `certs/nats`). Update README/`.env.example` text only where it describes these files.

**Acceptance checks for this point:**

- (a) Run `PATH=<dir containing only openssl>:/usr/bin:/bin bash deploy/generate-nats-certs.sh --force` (the
  openssl branch) from a scratch copy. Afterwards `deploy/certs/nats/` contains no CA private key, and
  `nats-key.pem` is `-rw-------`.
- (b) With the regenerated files, the NATS container built from `deploy/nats/nats-server.conf` starts and logs
  `TLS required for client connections`. A service container using the new single-file mount connects with
  `NatsTlsCaFile=/etc/nats/certs/ca.pem`. `docker exec <app container> ls /etc/nats/certs` lists only `ca.pem`.
- (c) Every generated key path is git-ignored (`git check-ignore -v …`) and `git status` shows no key material.

---

## What I verified as correct (do not change these)

Environment: `dotnet build` at HEAD; a throwaway Postgres container; a NATS container running the repo's
`deploy/nats/nats-server.conf` with certificates from `deploy/generate-nats-certs.sh` (openssl branch);
Identity.Service and Users.Service running against them with `DOTNET_ENVIRONMENT=Local` and
`NatsTlsCaFile` set; Identity.WebApi (HEAD build output) running in a Linux container on the same Docker
network, reaching NATS as `nats:4222` over TLS.

- **AC7 build:** `dotnet build src/backend/Core/Core.sln` → Build succeeded, 0 errors (only the pre-existing
  2× NU1903 AutoMapper warnings).
- **AC1 no password on the login path / bus / DTO:** no `Password` member in any `*.Contracts` project; no
  password generation; `TemporaryPasswordGenerator`, `SetIdentityUserPassword*`, `GenerateToken*`,
  `ITokenService`/`TokenService` are gone with no remaining references (`git grep`, successful build).
  Handlers are resolved by type name at runtime (`NatsCommandBus.cs:25-31`), so no stale registration list exists.
- **AC2 ticket properties:**
  32 bytes from `RandomNumberGenerator`; only `base64(SHA-256)` is stored; the comparison uses
  `CryptographicOperations.FixedTimeEquals` over fixed-length digests. The row is bound to one `UserId`, and
  extra form fields cannot retarget it (verified `sub` = row user). TTL: with
  `Authentication__AuthTicket__LifetimeSeconds=3600` the issued row had `ExpiresAt − CreatedAt = 60 s` (the
  clamp works). Expired, pre-redeemed, forged-secret, unknown-id, malformed and missing tickets all get
  `400 invalid_grant`. A forged attempt does not burn the genuine ticket. **Atomic single use proven in
  Postgres:** session A ran the exact `UPDATE … WHERE "Id"=… AND "RedeemedAt" IS NULL AND "ExpiresAt" > …`
  inside an open transaction while 8 concurrent sessions ran the same statement. All 8 blocked, then returned
  `UPDATE 0`, and A returned `UPDATE 1`. Over HTTP, 8 rounds × 40 concurrent redemptions of one ticket never
  produced more than one 200.
- **AC3:** `UserDto`, `CreateUserCommand`, `UpdateUserCommand`, `CreateIdentityUserCommand` carry no password.
  Over the live TLS bus, `UserQuery` (GetUser) and `UpdateUserCommand` return the user without any credential
  field. (`CreateUserCommand` fails with `null value in column "UserName"`: pre-existing, see N7.)
- **AC4:** two logins for the same e-mail both return 200 with the same `sub`. After the second login, the
  **first** session's refresh token still returns 200. The identity row has `PasswordHash = NULL`.
- **AC5 (apart from blocking point 2):** the server logs `TLS required for client connections`. Probes through
  the real `NatsConnectionFactory.Create`:
  pinned CA + `localhost` → connected (TLS); pinned CA + `127.0.0.1` → connected;
  other CA → refused; no CA file (system trust) → refused; host not in SAN (container IP) → refused;
  `NatsTlsEnabled=false` → refused; wrong password → `Authorization Violation`; no credentials →
  `Authorization Violation` (§2 auth still enforced); missing CA path → `FileNotFoundException`.
  `deploy/certs/.gitignore` ignores `*.pem` (checked with `git check-ignore -v`). `scripts/deploy_all_stage.sh`
  copies the script, not key material. `start_stage.sh` fails fast without certificates. The `.run/*.run.xml`
  path `$PROJECT_DIR$/../../../deploy/certs/nats/ca.pem` resolves to the file the script writes.
- **AC6 migration:** `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the
  model since the last migration". Designer and snapshot are consistent. Up SQL: `DROP NOT NULL`,
  `UPDATE "Users" SET "PasswordHash" = NULL`, `CREATE TABLE "AuthTickets"` + index. Both migrations applied
  cleanly to an empty database. Clearing the hashes is justified (they were temporary passwords broadcast over
  the unencrypted bus; users never knew them).
- **§6 preserved (review item D):** `VerifyAuthRequestHandler` keeps expiry, `AttemptCount` lockout,
  `FixedTimeEquals` over SHA-256, and `ConsumeAsync` (delete) before any ticket is requested; only the tail
  changed. Live: the auth request row is gone after a successful login.
- **Rate limiting (review item C):** 13 wrong codes for one `id` → `[401×10, 429×3]`. The subject partition
  still sees the original `id` because `UseTokenSubjectPartitionKey` runs before the middleware
  (`Program.cs:66-75`).
- **Password grant left enabled (review item E):** dead but harmless. `ValidateUserAsync` returns null for an
  empty/null hash (`IdentityService.cs:41`), and the grant was refused live for a passwordless account.
  `SetUserPasswordAsync` has no caller anywhere (`git grep`). No bus or HTTP path can set a password.
- **Signing-key literal (review item I):** the only reader is the developer-profile branch
  (`Identity.WebApi/Extensions.cs:87-91`). Stage sets `ASPNETCORE_ENVIRONMENT=Stage`
  (`docker-compose.app.stage.yml:25`), so it does not affect non-developer environments.

---

## Non-blocking observations (do NOT gate on these; several belong to other tasks)

- **N1 — any bus client can mint a login for any user.** From a plain `NatsCommandBus` client holding the shared
  bus credential I sent `IssueAuthTicketCommand { UserId = <any>, AuthRequestId = <random Guid> }` and received a
  valid ticket. Identity cannot verify that the auth request was actually verified (`IssueAuthTicketHandler.cs`,
  `AuthTicketService.IssueAsync`). This is strictly narrower than the removed `SetIdentityUserPasswordCommand`
  (persistent account takeover), and it stems from the single shared NATS credential with no subject
  permissions, which no P1/P2 item tracks today. Follow-up: per-service NATS users with publish/subscribe
  permissions (only Users.Service may publish `IdentityService.>` for this command).
- **N2 — `IdentityDbContext` is a Singleton (pre-existing, since `init`).** `Shared.Infrastructure/Extensions.cs:52`
  invokes `AddDbContext` by reflection with `null` for both `ServiceLifetime` arguments, which becomes
  `default(ServiceLifetime)` = `Singleton` (confirmed by inspecting the `ServiceDescriptor`s). Concurrent requests
  fail ("A second operation was started on this context instance…"; 39 of 40 concurrent logins failed), and
  the EF message leaks in the 400 body. It affects every service. Fix in a separate task (pass
  `ServiceLifetime.Scoped` explicitly and check for captive dependencies). Do not fold it into this rework.
- **N3 — password grant still registered.** `AllowPasswordFlow` (`Identity.WebApi/Extensions.cs:65`),
  `gt:password` (`AppInitializer.cs:17`), `ExchangePasswordAsync` and `SetUserPasswordAsync` could be removed in a
  follow-up to shrink the surface. Harmless today.
- **N4 — migration `Down` is lossy.** It sets `''` for NULL hashes and cannot restore the cleared ones. That is
  inherent, but a one-line comment in `Down` would make it explicit.
- **N5 — expiry uses two application clocks.** `ExpiresAt` comes from Identity.Service's clock and is
  compared with Identity.WebApi's clock (`AuthTicketRepository.TryRedeemAsync` binds `now` as a parameter).
  Node clock skew changes the effective TTL. Using the database clock (`now()`) would remove that.
- **N6 — CA "pinning" is additive.** `NatsConnectionFactory` accepts `SslPolicyErrors.None` before consulting the
  custom root, so a system-trusted certificate with a matching name is also accepted. Negligible for
  `nats`/`localhost`.
- **N7 — `CreateUserCommand` is broken (pre-existing).** `UserName` is not mapped
  (`Users.Application/MappingProfile.cs:13`), so the insert violates NOT NULL. That file is unchanged since the
  parent commit, and the removed `Password` was never mapped.
- **N8 — local run on macOS.** Identity.WebApi started from a terminal with `DOTNET_ENVIRONMENT=Local` hung after
  inserting the token rows (development signing certificate path; not investigated further). The same HEAD
  build output works in a Linux container. Useful when re-verifying: mount a *writable copy* of
  `Identity.WebApi/bin/Debug/net9.0` into a .NET 9 ASP.NET runtime image on a Docker network where the NATS
  container has alias `nats` (the certificate SAN).
- **N9 — reissue replaces the CA.** On the openssl branch a reissue creates a new CA, and running services keep
  the old CA in memory until restarted. Worth one line in the script output.
- Pre-existing, already known: `VerifyAuthToTokenMiddleware` resolves `IUserService` (and the NATS connection)
  for every `/connect/token` POST; `NatsCommandBus` request timeout is `5000 * 1000` ms (P1).

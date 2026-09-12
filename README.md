# Core

Reusable .NET 9 backend core: HTTP gateway, authentication, user registry, file storage and one reference
vertical service to copy when adding domain logic. Deployed as a Docker Swarm stack.

## Project Structure

```
src/backend/Core/Core.sln          # 28 projects
├── Gateways/Gateway.WebApi        # HTTP entry point: File, Samples, Users controllers
├── Services/
│   ├── Identity/                  # OpenIddict server: Api, Application, Contracts, Domain,
│   │                              #   Infrastructure, Service, WebApi
│   ├── Users/                     # User registry: Api, Application, Contracts, Domain,
│   │                              #   Infrastructure, Service
│   └── Sample/                    # Reference vertical, copy it to add a new one
└── Shared/                        # Shared, .Domain, .Application, .Infrastructure,
                                   #   .Messaging, .Api, .WebApi, .Service
build/backend/                     # Dockerfiles: Gateway, Identity, Identity.WebApi, Users, Sample
scripts/                           # Build, publish, and helper scripts
deploy/                            # Docker Compose files and deploy scripts
project.env                        # Single place to rename the product
```

Each vertical follows the same layering: `Contracts` (commands, queries, DTOs) → `Domain` (aggregate,
repository interface) → `Application` (MediatR handlers, AutoMapper profile) → `Infrastructure` (EF Core
DbContext, repository, migrations) → `Api` (typed client over the NATS bus) → `Service` (standalone worker
host). The gateway talks to services only through the `Api` projects and the NATS messaging bus.

## project.env

`project.env` in the repository root is the single point of configuration when starting a new product on
this core. Every script in `scripts/` and `deploy/` sources it, and the compose files read the same
variables through `${...}` interpolation.

| Variable         | Default              | Meaning                                                     |
| ---------------- | -------------------- | ----------------------------------------------------------- |
| `REGISTRY`       | `ghcr.io/mohctpuk`   | Container registry images are tagged and pushed to           |
| `IMAGE_PREFIX`   | `core`               | Image name prefix: `${REGISTRY}/${IMAGE_PREFIX}-gateway`     |
| `LOCAL_DOMAIN`   | `coreapp.local`      | Local dev domain: `api.${LOCAL_DOMAIN}`, `auth.${LOCAL_DOMAIN}` |
| `STAGE_DOMAIN`   | see `project.env`    | Public DNS name of the stage server                          |
| `STAGE_SSH_HOST` | see `project.env`    | SSH target for stage deploys                                 |
| `STAGE_SSH_PORT` | `2222`               | SSH port for stage deploys                                   |

To rebrand the product, change these values, re-run `create_local_domain.sh` and `generate-local-certs.sh`,
then rebuild and republish the images under the new prefix.

`project.env` holds **non-secret** naming only. Credentials live in `deploy/.env` — see below.

## Infrastructure credentials (`deploy/.env`)

Nothing in git carries a working password. Infrastructure credentials come from `deploy/.env`, which is
created from `deploy/.env.example` on the machine that runs the stack and is git-ignored. Both
`start_local.sh` and `start_stage.sh` source it if it exists.

| Variable | Consumers | Meaning |
| --- | --- | --- |
| `POSTGRES_USER`, `POSTGRES_PASSWORD` | `postgres`; `users`, `sample`, `identity`, `identity-webapi` | Database login. Substituted into `ConnectionStrings__DefaultConnection`, so avoid `;`, `'` and `"` in the password |
| `NATS_USER`, `NATS_PASSWORD` | `nats`; all five services | Bus login. The bus rejects unauthenticated clients, and requires TLS on top (see [NATS TLS](#nats-tls)) |
| `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD` | `minio`; `gateway` | Object storage root credentials |
| `REDIS_CONNECTION_STRING` | `identity-webapi` | Where the DataProtection key ring lives. `redis:6379` by default — the Redis of the infra stack |
| `TRAEFIK_LOG_LEVEL` | `traefik` | `INFO` by default. `DEBUG` logs request headers — never on stage |
| `TRAEFIK_DASHBOARD_HOST`, `TRAEFIK_DASHBOARD_AUTH` | `traefik` | Only for the optional dashboard override (see below) |

**The local stand needs no `.env`.** Dev defaults live in the local overrides only —
`docker-compose.infra.local.yml`, `docker-compose.app.local.yml`,
`docker-compose.management.local.yml` and the `appsettings.Local.json` files. A `deploy/.env`, if present,
overrides them.

**Stage requires it.** `start_stage.sh` aborts before deploying anything if one of the six credential
variables is empty, and the stage compose files fail interpolation on their own (`${VAR:?…}`). No stage
service falls back to a default password.

> Rotating `POSTGRES_PASSWORD` on an existing stand: the official postgres image creates the role only on
> the **first** start against an empty volume. On a server whose `postgres_data` volume already exists,
> either set the variables to the values the volume was initialized with, or change the password in the
> database itself (`ALTER USER`) and in `deploy/.env` together.

### Where each stand gets service credentials

`appsettings.json` is shared with stage, so it holds no logins or passwords — only hosts, ports and
database names.

| Environment | Source of credentials |
| --- | --- |
| Docker local (`Development`) | `deploy/docker-compose.app.local.yml` |
| IDE run (`Local`) | `appsettings.Local.json` |
| Stage (`Stage`) | `deploy/docker-compose.app.stage.yml` ← `deploy/.env` |

### What is published to the host

| Stand | Published | Not published |
| --- | --- | --- |
| stage | `80/tcp`, `443/tcp`, `443/udp` — Traefik only | postgres, NATS, MinIO, Redis, Portainer, Traefik dashboard: overlay network only |
| local | Traefik `8181`/`8443`, Portainer `9003`/`8065`, PostgreSQL `6566`, NATS `4223`/`8223`, MinIO `9009`/`9001` | — |

`docker-compose.infra.yml` and `docker-compose.management.yml` declare **no** `ports:` at all; publishing
exists only in the `*.local.yml` files. Docker Swarm cannot bind a published port to `127.0.0.1` —
`docker stack deploy` rejects `host_ip`, and `mode: host` listens on every interface of the node — so the
local stand's infrastructure ports are reachable on every address of the developer machine. Do not run the
local stand on a host that faces the internet.

### Traefik dashboard

`--api.insecure=true` is used on no stand: it serves the dashboard, and the whole Traefik API, without a
password. The base compose enables the API (`--api.dashboard=true`) but declares no router, so the
dashboard is unreachable by default. To open it, set the two dashboard variables and add the optional
override:

```bash
htpasswd -nbB admin '<password>'     # paste the whole output line into TRAEFIK_DASHBOARD_AUTH
docker stack deploy \
  --compose-file docker-compose.infra.yml \
  --compose-file docker-compose.infra.stage.yml \
  --compose-file docker-compose.infra.dashboard.yml infra
```

The dashboard is then served on `https://${TRAEFIK_DASHBOARD_HOST}` through the normal `websecure`
entrypoint, behind a basic-auth middleware. Port `8080` is published nowhere.

### NATS authentication

`deploy/nats/nats-server.conf` makes the bus require a login. The file itself holds no secret:
nats-server expands `$NATS_USER` / `$NATS_PASSWORD` from the container environment. Clients read
`NatsUser` / `NatsPassword` from configuration
(`Shared/Shared.Messaging/Messaging/NatsConnectionFactory.cs`); credentials embedded in `NatsUrl`
(`nats://user:password@host:4222`) work as well. Without them the server answers
`Authorization Violation`, which the connection factory prints to the log.

### NATS TLS

The bus also refuses plaintext connections. `deploy/nats/nats-server.conf` carries a `tls { … }` block, and
the server logs `TLS required for client connections` on start; a client that connects without TLS is
dropped during the handshake.

The certificate is never committed. `deploy/generate-nats-certs.sh` issues it into `deploy/certs/nats/`
(SAN `nats`, `localhost`, `127.0.0.1`) and is idempotent: it reuses an existing, unexpired certificate and
only reissues when asked with `--force`. It signs with the machine's `mkcert` CA when that is installed and
falls back to a private CA created by `openssl` otherwise. Either way the root lands in
`deploy/certs/nats/ca.pem`, and every client is pointed at that file — containers through
`NatsTlsCaFile=/etc/nats/certs/ca.pem`, IDE runs through the `NatsTlsCaFile` entry in
`src/backend/Core/.run/*.run.xml`. Do not count on the system trust store: even a `mkcert` root that macOS
trusts is not honoured by .NET's default validation here.

Only the `nats` container mounts the whole `deploy/certs/nats/` directory; the application services mount the
single file `ca.pem`, so no private key reaches them. The server key `nats-key.pem` is `0600` (the `nats`
image runs as root). No CA private key is kept in `deploy/certs/nats/`: `mkcert` keeps its own under
`mkcert -CAROOT`, and the `openssl` fallback creates its CA in a temporary directory and deletes the key right
after signing. As a consequence a reissue on the `openssl` branch creates a new CA, so restart `nats` and every
application service after `--force` — they keep the previous `ca.pem` until they restart.

`start_local.sh` runs the script for you. On stage the certificate is issued **on the server** —
`deploy_all_stage.sh` copies the script but never the key material, and `start_stage.sh` refuses to deploy
when `deploy/certs/nats/` is empty.

Clients read three keys (`Shared/Shared.Messaging/Messaging/NatsConnectionFactory.cs`):

| Key | Default | Meaning |
| --- | --- | --- |
| `NatsTlsEnabled` | `true` | TLS is on unless something explicitly sets `false` |
| `NatsTlsCaFile` | empty | Root certificate to verify the bus against. Empty → the system trust store |
| `NatsUser` / `NatsPassword` | empty | Bus login, still required on top of TLS |

`NatsTlsCaFile` narrows which root is trusted; it does not disable verification. The host name and validity
of the server certificate are checked either way, and a certificate signed by an unknown CA is refused.

### Token signing keys and DataProtection

OpenIddict needs two certificates — one to sign tokens, one to encrypt them — and ASP.NET Core
DataProtection needs a key ring. Both used to live inside the container: the development certificate was
generated into the container user's profile on **every** environment, and the key ring went to
`LocalApplicationData`. Neither survived a restart, and with more than one replica each replica had its
own copy, so a token issued by one was rejected by the other.

| Environment | Signing / encryption certificates | DataProtection key ring |
| --- | --- | --- |
| `Development`, `Local` | `AddDevelopmentSigningCertificate()` / `AddDevelopmentEncryptionCertificate()` | Docker local stand: shared Redis. IDE run (`Local`): `~/.local/share/Core/DataProtection-Keys` — Redis is not published to the host |
| `Stage`, `Production` | Real PKCS#12 certificates from docker secrets (or an X509 store entry) | Shared Redis, key ring encrypted with the encryption certificate |

Development certificates are used **only** when the environment is `Development` or `Local`
(`Identity.WebApi/Extensions.cs`). Any other environment loads real certificates and, if the material is
missing or unreadable, **fails on startup** with an explicit message instead of silently falling back to a
throwaway key.

Create the material once per server:

```bash
bash deploy/generate-identity-certs.sh --create-secrets
```

It writes two self-signed PKCS#12 bundles with random passwords into `deploy/certs/` (git-ignored) and
registers four Swarm secrets:

| Secret | Mounted at | Read through |
| --- | --- | --- |
| `identity_signing_certificate` | `/run/secrets/identity_signing_certificate` | `OpenIddict__SigningCertificate__Path` |
| `identity_signing_certificate_password` | `/run/secrets/identity_signing_certificate_password` | `OpenIddict__SigningCertificate__PasswordFile` |
| `identity_encryption_certificate` | `/run/secrets/identity_encryption_certificate` | `OpenIddict__EncryptionCertificate__Path` |
| `identity_encryption_certificate_password` | `/run/secrets/identity_encryption_certificate_password` | `OpenIddict__EncryptionCertificate__PasswordFile` |

`docker-compose.app.stage.yml` declares the secrets as `external: true`, so nothing about them is stored
in git, and `start_stage.sh` aborts before deploying if any of the four is missing. A certificate already
issued by another CA works just as well: point the same variables at its `.pfx`, or set
`OpenIddict__SigningCertificate__Thumbprint` (plus `StoreName`/`StoreLocation`) to use the X509 store.

The key ring lives in the Redis that is already part of the infra stack
(`DataProtection__RedisConnectionString`, `redis:6379` by default, overridable with
`REDIS_CONNECTION_STRING`). `SetApplicationName("core-app")` is what makes every replica share one ring —
without it ASP.NET Core derives the name from the content root, which differs per container. On stage the
ring is additionally encrypted at rest with the encryption certificate
(`ProtectKeysWithCertificate`/`UnprotectKeysWithAnyCertificate`), so a dump of Redis does not leak the
master key. `DataProtection__KeysDirectory` is the alternative for stands without Redis — it must point at
a volume mounted into every replica. In a non-development environment with neither configured, startup
fails rather than quietly writing keys into the container.

> **Rotation.** Replacing the encryption certificate makes the existing key ring undecryptable, and
> replacing the signing certificate invalidates issued access tokens. Both wipe active sessions, so rotate
> them at a moment when that is acceptable.

## Quick Start

### 1. Set up local domains

```bash
cd scripts
sudo ./create_local_domain.sh
```

Adds `api.coreapp.local` and `auth.coreapp.local` to `/etc/hosts`.

### 2. Generate local HTTPS certificates

```bash
cd deploy
./generate-local-certs.sh
```

Uses `mkcert` (preferred) or falls back to OpenSSL self-signed certs.

### 3. Build images

```bash
cd scripts
./build_all.sh
```

### 4. Deploy locally

```bash
cd deploy
./start_local.sh
```

### 5. Smoke test

```bash
curl -k -X POST https://api.coreapp.local:8443/Users/CreateAuthRequest \
  -H 'Content-Type: application/json' -d '{"email":"test@example.com"}'
```

The response contains only `id` and `email` — the verification code is **never** returned by this endpoint.
The code is delivered out-of-band (see [Verification code delivery](#verification-code-delivery)).

The local stack runs with `ASPNETCORE_ENVIRONMENT=Development` / `DOTNET_ENVIRONMENT=Development`, so the
code is written to the `users` service log and can also be read back through the dev-only endpoint:

```bash
curl -k https://api.coreapp.local:8443/dev/auth-requests/<id>/verification-code
```

That endpoint is mapped only when the host environment is `Development`, and the handler behind it refuses
the request in any other environment. On stage and in production it does not exist (404). The same code is
in the service log:

```bash
docker service logs core_users | grep DEVELOPMENT
```

Exchange the `id` and the `verificationCode` at `https://auth.coreapp.local:8443/connect/token` for an
access token, then call `POST /Samples/Create` with it.

Behind that endpoint the exchange never involves a password. `VerifyAuthToTokenMiddleware` sends the code to
the `users` service, which verifies it and asks Identity for a **single-use ticket**: bound to one user,
valid for at most 60 seconds, stored only as a SHA-256 digest, and spent by the first redemption. The
middleware then rewrites the request as the internal `auth_request` grant and the token controller redeems
the ticket. The ticket reaches the controller only through server-side request state (`HttpContext.Items`),
never as a form field: it does not appear in OpenIddict's request log, and a caller that posts
`grant_type=auth_request` to `/connect/token` itself gets `invalid_grant`. Accounts opened this way have no
password at all, so logging in a second time cannot invalidate the first session.

## Verification code delivery

`POST /Users/CreateAuthRequest` generates a verification code, stores it, and hands it to
`IVerificationCodeSender` (`Users.Application/Interfaces/IVerificationCodeSender.cs`). The code is a shared
secret that is exchanged for an access token, so it leaves the system only through that channel — never in
an HTTP response body.

The implementation is chosen by environment in `Users.Infrastructure/Extensions.cs`, not by a configuration
flag:

| Environment | Implementation | Behaviour |
| --- | --- | --- |
| `Development`, `Local` | `DevelopmentVerificationCodeSender` | Logs the code at `Warning` level; no mail server needed |
| everything else (`Stage`, `Production`, …) | `SmtpVerificationCodeSender` | Mails the code over SMTP |

`Local` is the profile the IDE run configurations in `src/backend/Core/.run/` use. The dev-only endpoint
below is narrower: it is mapped **only** under `Development`. When running the services from the IDE under
`Local`, read the code from the service console output.

SMTP is configured through the `Notifications:Smtp` section of the `users` service configuration
(`Services/Users/Users.Service/appsettings*.json`):

| Key | Default | Meaning |
| --- | --- | --- |
| `Notifications:Smtp:Host` | empty | SMTP server host. Empty → sending fails with a clear error |
| `Notifications:Smtp:Port` | `587` | SMTP port |
| `Notifications:Smtp:EnableSsl` | `true` | Use STARTTLS/SSL |
| `Notifications:Smtp:UserName` | empty | SMTP login. Empty → default credentials |
| `Notifications:Smtp:Password` | empty | SMTP password. **Never commit it** |
| `Notifications:Smtp:FromAddress` | `no-reply@example.com` | Envelope/from address |
| `Notifications:Smtp:FromDisplayName` | `Core` | Display name of the sender |
| `Notifications:Smtp:Subject` | `Your verification code` | Message subject |
| `Notifications:Smtp:BodyTemplate` | `Your verification code is {code}.` | Body; `{code}` is substituted |
| `Notifications:Smtp:TimeoutMilliseconds` | `10000` | Send timeout |

The values committed to `appsettings.json` / `appsettings.Stage.json` are non-secret placeholders. Supply
the real credentials at deploy time through environment variables or Docker secrets, which override the
file values:

```bash
Notifications__Smtp__Host=smtp.your-provider.example
Notifications__Smtp__UserName=...
Notifications__Smtp__Password=...
```

## Common Flows

### Full rebuild + deploy

```bash
cd scripts && ./build_all.sh
cd ../deploy && ./start_local.sh
```

### Rebuild backend only + redeploy app stack

```bash
cd scripts && ./redeploy_backend_local.sh
```

Or manually: `./build_backend.sh` then `../deploy/start_local.sh`. The script runs `deploy/stop.sh` →
`build_backend.sh` → `deploy/start_local.sh`.

### Switch Gateway to local (for debugging)

```bash
cd deploy
./switch-gateway-mode.sh local    # routes traffic to host.docker.internal:5115
./switch-gateway-mode.sh docker   # restores Docker gateway service
./switch-gateway-mode.sh status   # show current mode
```

In `local` mode, run `src/backend/Core/Gateways/Gateway.WebApi` from the IDE and Traefik forwards
`api.coreapp.local` to it.

### Publish images to GHCR

```bash
cd scripts
./publish_all.sh                          # linux/amd64
./publish_all.sh linux/amd64,linux/arm64  # multi-platform
```

### Deploy to stage

```bash
cd scripts
./deploy_all_stage.sh
```

Copies the compose files, `deploy/nats/`, `.env.example`, `start_stage.sh`, `generate-nats-certs.sh` and
`project.env` via SCP, then
runs `start_stage.sh` on the server. Always publish before deploying: stage pulls images from the registry,
it does not build them.

`deploy/.env` is deliberately **not** copied: it holds the real infrastructure passwords and is created once
on the server. Until it exists, `start_stage.sh` refuses to deploy.

## How to Add a New Vertical

`Services/Sample` is the template. It is a minimal but complete vertical, so copy it rather than writing a
service from scratch.

1. Copy `src/backend/Core/Services/Sample` to `src/backend/Core/Services/<Name>` and rename every
   `Sample` occurrence in file names, project names, namespaces and class names.
2. Model the aggregate in `<Name>.Domain/Aggregates/<Name>Aggregate.cs` and the commands, query filters and
   DTO in `<Name>.Contracts`.
3. Adjust the MediatR handlers in `<Name>.Application`. Create, Update and Delete are three-line subclasses
   of the generic handlers in `Shared.Application/Handlers/`; only `Get<Name>Handler` needs a real
   `FilterExpression`.
4. In `<Name>.Infrastructure`, point the DbContext at its own database, then generate the initial migration:
   `dotnet ef migrations add Initial -p <Name>.Infrastructure -s <Name>.Service`.
5. Set the NATS subject in `<Name>.Api/MessagingSubject.cs` and list it under `NatsStreams` in the four
   `<Name>.Service/appsettings*.json` files.
6. Register the six projects in `Core.sln` under a solution folder, add
   `builder.Services.Add<Name>Api(builder.Configuration)` to `Gateways/Gateway.WebApi/Program.cs`, and add a
   `<Name>sController` next to `SamplesController.cs`.
7. Copy `build/backend/Sample.Dockerfile` to `<Name>.Dockerfile`, add the service to `build_backend.sh`,
   `publish_all.sh`, `deploy/start_stage.sh` and the two app compose files, and add
   `COPY ./Services/<Name> ./Services/<Name>` to `Gateway.Dockerfile`.

## Local Access (localhost)

### Application (via Traefik)

| Service          | URL                                     |
| ---------------- | --------------------------------------- |
| Backend API      | https://api.coreapp.local:8443          |
| Identity         | https://auth.coreapp.local:8443         |

### Infrastructure

| Service            | URL / Port                    |
| ------------------ | ----------------------------- |
| Traefik HTTP       | http://localhost:8181         |
| Traefik HTTPS      | https://localhost:8443        |
| Traefik Dashboard  | not published — see [Traefik dashboard](#traefik-dashboard) |
| Portainer UI       | http://localhost:9003         |
| PostgreSQL         | localhost:6566                |
| NATS Client        | localhost:4223 (TLS + login required) |
| NATS Monitoring    | http://localhost:8223         |
| MinIO API          | http://localhost:9009         |
| MinIO Console      | http://localhost:9001         |

These ports exist on the **local stand only**. The stage stand publishes nothing but `80` and `443`; reach
its infrastructure through an SSH tunnel or `docker exec`.

## Deployment Order

1. **Management** stack — Portainer
2. **Infrastructure** stack — Traefik, PostgreSQL, Redis, NATS, MinIO
3. **Application** stack — Gateway, Identity, Identity.WebApi, Users, Sample

There is no separate database step. On startup every service creates its database (`identity`, `users`,
`sample`) if it is missing and applies migrations (`Shared.Infrastructure/EfCoreDatabaseMigrator.cs`), waiting
for PostgreSQL to come up. The login from `ConnectionStrings__DefaultConnection` therefore needs the `CREATEDB`
privilege — `POSTGRES_USER` has it, since the postgres image creates it as a superuser.

## Scripts Reference

### `scripts/` — Build & Publish

All scripts must be run from the `scripts/` directory. Build scripts accept an optional `platform` argument
(auto-detected from CPU arch if omitted).

| Script                        | Description                                                          |
| ----------------------------- | -------------------------------------------------------------------- |
| `build_all.sh [platform]`     | Builds every image; currently delegates to `build_backend.sh`         |
| `build_backend.sh [platform]` | Builds Gateway, Identity, Identity.WebApi, Users, Sample images       |
| `publish_all.sh [platforms]`  | Builds and pushes all five images to GHCR (`linux/amd64` by default)  |

### `scripts/` — Helpers

| Script                      | Description                                                                  |
| --------------------------- | ---------------------------------------------------------------------------- |
| `redeploy_backend_local.sh` | Stop stacks, build backend images, start local deploy (run from `scripts/`)   |
| `create_local_domain.sh`    | Adds `api.` and `auth.` `${LOCAL_DOMAIN}` entries to `/etc/hosts` (needs `sudo`) |
| `remove_all.sh`             | Removes all stacks and leaves Docker Swarm                                    |

### `scripts/` — Stage Deploy

| Script                | Description                                                                |
| --------------------- | -------------------------------------------------------------------------- |
| `deploy_all_stage.sh` | Copies compose files, `nats/`, `.env.example`, `start_stage.sh`, `generate-identity-certs.sh`, `generate-nats-certs.sh` and `project.env`, runs `start_stage.sh`. Never copies `deploy/.env`, the generated certificates or the Swarm secrets |

### `deploy/` — Docker Swarm

| Script                              | Description                                                    |
| ----------------------------------- | -------------------------------------------------------------- |
| `start_local.sh`                    | Inits Swarm (if needed), issues the NATS TLS certificate, deploys management + infra + app |
| `start_stage.sh`                    | Pulls images from GHCR, deploys all stacks for stage           |
| `stop.sh`                           | Removes app, infra, and management stacks                      |
| `switch-gateway-mode.sh <mode>`     | Switches Gateway between `docker`, `local`, or shows `status`  |
| `generate-identity-certs.sh`        | Issues the OpenIddict signing/encryption PKCS#12 bundles; `--create-secrets` registers them as Swarm secrets |
| `generate-nats-certs.sh`            | Issues the TLS certificate for the bus into `deploy/certs/nats/`. Idempotent; `--force` reissues |
| `generate-local-certs.sh`           | Generates local HTTPS certs via mkcert or OpenSSL, then the NATS certificate |
| `generate-local-certs.ps1`          | Same as above, PowerShell version for Windows                  |

### `deploy/` — Compose Files

| File                                  | Description                                                                 |
| ------------------------------------- | --------------------------------------------------------------------------- |
| `docker-compose.management.yml`       | Portainer. No published ports                                               |
| `docker-compose.management.local.yml` | Local publishing of the Portainer UI (`9003`, `8065`)                       |
| `docker-compose.infra.yml`            | Traefik, PostgreSQL, Redis, NATS, MinIO (base). No published ports, no credentials |
| `docker-compose.infra.local.yml`      | Local publishing of infra ports + dev credentials                           |
| `docker-compose.infra.stage.yml`      | Stage overrides: LetsEncrypt, `80`/`443` only, credentials required from `.env` |
| `docker-compose.infra.dashboard.yml`  | Optional: Traefik dashboard behind basic-auth. Not deployed by default      |
| `docker-compose.app.yml`              | Gateway, Identity, Identity.WebApi, Users, Sample (base)                    |
| `docker-compose.app.local.yml`        | Dev credentials for the local Docker stand                                  |
| `docker-compose.app.stage.yml`        | Stage overrides: public domains, `Stage` environment, credentials from `.env`, OpenIddict certificates from Swarm secrets |

| File                    | Description                                                              |
| ----------------------- | ------------------------------------------------------------------------ |
| `.env.example`          | Template for `deploy/.env`. Placeholders only, no real values            |
| `nats/nats-server.conf` | NATS config: authentication via `$NATS_USER` / `$NATS_PASSWORD` and required TLS (`deploy/certs/nats/`) |

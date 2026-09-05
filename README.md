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

Copies the compose files, `start_stage.sh` and `project.env` via SCP, then runs `start_stage.sh` on the
server. Always publish before deploying: stage pulls images from the registry, it does not build them.

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
| Traefik Dashboard  | http://localhost:9002         |
| Portainer UI       | http://localhost:9003         |
| PostgreSQL         | localhost:6566                |
| NATS Client        | localhost:4223                |
| NATS Monitoring    | http://localhost:8223         |
| MinIO API          | http://localhost:9009         |
| MinIO Console      | http://localhost:9001         |

## Deployment Order

1. **Management** stack — Portainer
2. **Infrastructure** stack — Traefik, PostgreSQL, Redis, NATS, MinIO
3. **Application** stack — Gateway, Identity, Identity.WebApi, Users, Sample

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
| `deploy_all_stage.sh` | Copies compose files, `start_stage.sh` and `project.env`, runs `start_stage.sh` |

### `deploy/` — Docker Swarm

| Script                              | Description                                                    |
| ----------------------------------- | -------------------------------------------------------------- |
| `start_local.sh`                    | Inits Swarm (if needed), deploys management + infra + app      |
| `start_stage.sh`                    | Pulls images from GHCR, deploys all stacks for stage           |
| `stop.sh`                           | Removes app, infra, and management stacks                      |
| `switch-gateway-mode.sh <mode>`     | Switches Gateway between `docker`, `local`, or shows `status`  |
| `generate-local-certs.sh`           | Generates local HTTPS certs via mkcert or OpenSSL              |
| `generate-local-certs.ps1`          | Same as above, PowerShell version for Windows                  |

### `deploy/` — Compose Files

| File                              | Description                                      |
| --------------------------------- | ------------------------------------------------ |
| `docker-compose.management.yml`   | Portainer                                        |
| `docker-compose.infra.yml`        | Traefik, PostgreSQL, Redis, NATS, MinIO (base)   |
| `docker-compose.infra.local.yml`  | Local port overrides for infra                   |
| `docker-compose.infra.stage.yml`  | Stage overrides (LetsEncrypt, public ports)      |
| `docker-compose.app.yml`          | Gateway, Identity, Identity.WebApi, Users, Sample (base) |
| `docker-compose.app.stage.yml`    | Stage overrides (public domains, `Stage` environment) |

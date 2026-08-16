# CI/CD Pipeline Configuration

> * **Project:** Pocket-Money
> * **Version:** 1.0
> * **Date:** 16 August 2026
> * **Target Release:** V1
> * **Document Status:** Approved
> * **Companion Docs:** SRS v1.0 · SDS v1.0 (source of truth) · SAD v1.0 · API Specification v1.0
> * **Scope:** Continuous Integration (GitHub Actions) and Continuous Deployment (Railway) for the V1 release. Local development workflow is out of scope except where it intersects the pipeline.

## 1. Overview

Every change flows through one path:

```
 developer (WSL / Windows)
      │  git push
      ▼
 GitHub  ──►  CI: GitHub Actions (build + tests)
      │              │ gate: all jobs green
      ▼              ▼
 Railway ◄──  CD: GitHub Autodeploy (waits for Actions)
      │
      ├── Service: api        (Dockerfile build, /healthz)
      ├── Service: postgres   (Railway plugin)
      └── Service: client     (static Blazor WASM assets)
```

Principles:

* `main` is always deployable. No manual deploy steps; merge = deploy.
* CI is the **only** quality gate — Railway deploys only after Actions passes.
* No secrets in the repo. All configuration via environment variables (SDS §6.3).
* .NET SDK version pinned in `global.json`; Docker base images pinned to the same version (currently .NET 11 Preview; re-pin at GA in November 2026).

## 2. Environments

| Environment | Where | Purpose | Database |
| --- | --- | --- | --- |
| Local | WSL (`dotnet run`) | Development | Windows-hosted Postgres (Docker), `localhost:5432` |
| CI | GitHub Actions runner | Build & test on every push/PR | Ephemeral Postgres service container |
| Production | Railway | Deployed V1 | Railway Postgres plugin (managed) |

V1 ships with a **single Railway environment (production)**. A staging environment (cloned Railway environment) is a post-V1 option, not a V1 cost.

## 3. Continuous Integration — GitHub Actions

### 3.1 Workflow

One workflow file: `.github/workflows/ci.yml`.

* **Triggers:** push to `main`, pull request targeting `main`.
* **Runner:** `ubuntu-latest`.

### 3.2 Jobs

| Job | Steps | Notes |
| --- | --- | --- |
| `api` | checkout → setup-dotnet (version from `global.json`) → `dotnet restore` → `dotnet build -c Release` → `dotnet test` | `dotnet test` runs both unit and integration test projects. Integration tests get an **ephemeral Postgres service container** (`postgres:17`) with a fresh schema per run via EF migrations. |
| `client` | checkout → setup-dotnet → `dotnet publish PocketMoney.Client -c Release` | Proves the Blazor WASM app compiles and publishes. Published artifacts are uploaded as workflow artifacts for traceability. |

Quality gates (both jobs must pass before Railway may deploy):

1. Build succeeds with zero errors.
2. All tests pass.
3. Treat compiler warnings seriously — the build does **not** fail on warnings in V1, but new warnings must be justified in review.

### 3.3 Secrets in CI

None required in V1: integration tests use the service-container Postgres with throwaway credentials defined inline in the workflow. Firebase Admin SDK is not exercised in CI (auth integration is covered by manual E2E acceptance, §6).

## 4. Continuous Deployment — Railway

### 4.1 Services

| Service | Source | Notes |
| --- | --- | --- |
| `api` | `Dockerfile` (multi-stage) at repo root | ASP.NET Core 11 Minimal APIs + SignalR `/hubs/ledger`. |
| `postgres` | Railway Postgres plugin | Connection string injected into `api` via Railway variable reference. |
| `client` | Static service (see open decision D-1) | Blazor WASM published output. |

### 4.2 Dockerfile (API)

Multi-stage build:

```
sdk:11.0-preview     ──► restore + publish PocketMoney.Api
runtime:11.0-preview ──► final image: explicit WORKDIR + non-root USER, EXPOSE 8080
```

* Final stage sets both explicitly — `WORKDIR /app` and `USER app` (a dedicated non-root user created in the image) — reinforcing non-root execution and a predictable runtime context regardless of base-image defaults.
* Base image tags pinned and bumped deliberately (preview → `11.0` at GA).
* Health check path: `/healthz` (returns 200 when API + DB connectivity OK). Railway healthcheck configured against it.

### 4.3 Deployment Flow

1. Push to `main` → GitHub Actions runs.
2. Railway **GitHub Autodeploy** is enabled with *"wait for GitHub Actions"* — the deployment sits in WAITING until CI is green, then builds and rolls out.
3. EF Core migrations are **applied automatically on API startup** (`Database.Migrate()` guarded by startup code). Single-instance V1 makes this safe; if we ever scale out, migrations move to a one-off deploy hook.
4. Railway keeps previous deployments — rollback is one click (no re-build needed).

### 4.4 Environment Variables (Railway)

Names only — values are set in the Railway dashboard, never committed (SDS §6.3):

| Variable | Purpose |
| --- | --- |
| `ConnectionStrings__PocketMoney` | Postgres (Railway variable reference) |
| `Jwt__Key` | Child-token signing key (≥ 256-bit random) |
| `Firebase__ProjectId` / `Firebase__ServiceAccount` | Admin SDK for parent ID-token verification |
| `Email__Provider` / `SendGrid__ApiKey` | Invitation emails (SDS §5.3) |
| `Security__IpBan__Enabled`, `Security__IpBan__*` | Ban-ladder tuning (SDS §10.2) |
| `Cors__AllowedOrigins` | Client origin URL(s) |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

**Proxy headers:** Railway routes traffic through a proxy. The API enables `UseForwardedHeaders` and trusts Railway's proxies so the IP-ban ladder (SDS §10.2) sees real client IPs, not the proxy's.

## 5. Versioning & Release

* **Semantic tags are automated, not manual:** a GitHub Action computes the next version (bump level from conventional-commit prefixes or a label on the merged PR), creates the tag on `main`, and publishes release notes. Manual tagging is error-prone; automation guarantees consistency and traceability. Tag = deploy marker in Railway (`v0.1.0`, …, `v1.0.0` for the V1 release).
* Release notes are generated per tag (checklist §5 "End-User Documentation / Release Notes").
* Rollback: Railway one-click redeploy of the previous deployment; no database down-migrations in V1 (forward-only migration policy).

## 6. V1 Acceptance (Definition of Deployed)

V1 is "deployed" when all of the following hold in the Railway production environment:

1. `/healthz` returns 200 behind the public URL.
2. Full manual E2E walkthrough of every FR in SRS §4–§7 passes on the deployed app (parent onboarding → invitations → children → transactions → child login incl. lockout ladder → SignalR live updates).
3. Forwarded-header behavior verified (ban ladder sees real IPs).
4. Client loads over HTTPS with the Ledger Paper theme intact (UI Spec §1.1).

## 7. Open Decisions

| ID | Decision | Status |
| --- | --- | --- |
| D-1 | Client static hosting: **Railway static service for V1.0** (single host, all three services in one project). Roadmap V1.x: migrate client to Cloudflare Pages (free CDN, better edge performance for Iran-based users) — the client is pure static files, so migration is a low-cost hosting change, not an architectural one. | ✅ Approved 2026-08-16 |
| D-2 | Migrations applied on API startup (`Database.Migrate()`), single-instance V1; **forward-only** rollback policy (redeploy old code, never run `Down()`; expand/contract pattern for destructive changes). | ✅ Approved 2026-08-16 |

## 8. Roadmap (V1.x / V2.0)

Deliberately deferred from V1 to keep the pipeline lean:

| Area | Item | Why deferred |
| --- | --- | --- |
| CI workflow | NuGet package caching (`actions/cache`) | V1 build times are short; caching pays off once the dependency graph grows |
| Testing | Coverage reporting (coverlet + reportgenerator) published as CI artifact / PR comment | Meaningful once the test suite is substantial enough to trend |
| Monitoring | Post-deploy observability: Railway logs + uptime/health alerts against `/healthz` | Completes the CI/CD feedback loop; add when real users depend on the deployment |
| Hosting | Client move to Cloudflare Pages (per D-1) | V1.0 ships all-railway for simplicity |
| Environments | Ephemeral Railway PR/branch environments for pre-merge QA | Worth it once more contributors or real users make prod-only acceptance insufficient |

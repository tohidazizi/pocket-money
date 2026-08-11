# Pocket-Money — System Architecture Design Document (ADD)

> 10 August 2026
> Version 1.0

* **Project:** Pocket-Money, Virtual Family Allowance & Ledger Web Application
* **Target Release:** V1 MVP
* **Document Status:** Draft for Review
* **Companion Docs:** SRS v1.0 (requirements) · SDS v1.0 (module-level design)

## 1. Purpose & Scope

This document describes the **macro view** of Pocket-Money V1: system context, logical components, runtime interactions, authentication model, multi-tenancy, deployment, and key architectural decisions. Module- and code-level details live in the SDS.

## 2. System Context

Two human actors (Parent = administrator, Child = viewer) interact with one web system. Two external SaaS dependencies: Firebase Auth (parent identity) and SendGrid (parent invitation emails).

```text
 ┌─────────┐   ┌─────────┐
 │ Parent  │   │  Child  │                ┌──────────────┐   ┌──────────┐
 │  (web)  │   │  (web)  │                │Firebase Auth │   │ SendGrid │
 └────┬────┘   └────┬────┘                │ (parent IdP) │   │ (email)  │
      │             │                     └──────▲───────┘   └────▲─────┘
      │  HTTPS      │  HTTPS                     │ verify         │ invite
      │  (SPA + REST + WebSocket)                │ ID tokens      │ emails
 ┌────▼─────────────▼────────────────────────────┴────────────────┴─────┐
 │                          POCKET-MONEY                                │
 │              Blazor WASM SPA + ASP.NET Core Web API                  │
 └──────────────────────────────┬───────────────────────────────────────┘
                                │ SQL (EF Core / Npgsql)
                      ┌─────────▼─────────┐
                      │   PostgreSQL 16+  │
                      └───────────────────┘
```

Notes:

* The SPA performs parent sign-in directly against Firebase Auth (email/password, Google SSO); the API never sees parent passwords — it only **validates Firebase ID tokens**.
* Children never touch Firebase; they authenticate against the Pocket-Money API with Account ID + PIN.

## 3. Logical Architecture

A decoupled SPA + monolithic API. Five .NET projects; `Shared` is referenced by both client and server so DTOs, enums, and Base-31 logic are written once.

```text
┌─────────────────────────── Browser ────────────────────────────┐
│  PocketMoney.Client (Blazor WASM)                              │
│   Parent dashboard │ Child dashboard │ Parent-PIN lock guard   │
│   Inactivity timer (5 min) │ 365-day child token storage       │
└──────────┬───────────────────────────────────┬─────────────────┘
           │ REST /api/v1 (JSON over HTTPS)    │ WSS /hubs/ledger
┌──────────▼───────────────────────────────────▼─────────────────┐
│  PocketMoney.Api (ASP.NET Core 11)                             │
│   Middleware pipeline:                                         │
│   IP-ban guard → AuthN (Firebase JWT │ child JWT + security    │
│   stamp check) → household scoping → controllers → audit log   │
│   Controllers: auth │ households │ children │ transactions     │
│   LedgerHub (SignalR): real-time balance/timeline push         │
├────────────────────────────────────────────────────────────────┤
│  PocketMoney.Domain + PocketMoney.Infrastructure               │
│   LedgerService (atomic credit/debit) │ LockoutService         │
│   AuditService │ Base31Generator │ EF Core DbContext, config,  │
│   migrations                                                   │
└──────────────────────────────┬─────────────────────────────────┘
                               │ Npgsql
                     ┌─────────▼─────────┐
                     │   PostgreSQL 16+  │
                     └───────────────────┘

  PocketMoney.Shared: DTOs, enums, constants, Base-31 utility
  (referenced by both Client and server-side projects)
```

## 4. Multi-Tenancy & Data Architecture

* **Tenant boundary:** `Household`. Every tenant-scoped row carries a `household_id` foreign key.
* **Enforcement:** defense in depth — (1) the API middleware resolves the caller's household from the verified token and scopes every query; (2) EF query-level scoping filters all tenant entities by `household_id`; (3) child sessions are additionally restricted to their own `child_id` (read-only).
* **Shape of the data:**

```text
 households (tenant boundary)
   ├─ 1──∞ parents            (max 2 per household)
   ├─ 1──∞ children           (max 9 per household)
   │            └─ 1──∞ transactions   (append-only ledger)
   ├─ 1──∞ household_invitations
   └─ 1──∞ audit_logs         (append-only)

 global (not tenant-scoped):  login_attempts, ip_bans
```

* **Ledger integrity:** `transactions` is append-only; corrections are new adjustment transactions. Each row stores a `remaining_after` snapshot, and `children.current_balance` is a cached running total updated atomically with the insert (see §6).

## 5. Authentication & Session Architecture

| Concern | Parent | Child |
| :--- | :--- | :--- |
| Identity provider | Firebase Auth (email/password, Google SSO) | Pocket-Money API (custom) |
| Credential | Firebase-managed password / SSO | 5-char Base-31 Account ID + 4-digit PIN |
| Session token | Firebase ID token | Custom JWT, **365-day** lifetime |
| Revocation | Firebase sign-out | `SecurityStamp` rotation on PIN reset → stale tokens rejected with 401; logout invalidates locally |
| Brute-force defense | Firebase-native | Progressive lockout (5 min → 15 min → permanent) + global IP ban ladder |
| Shared-device guard | 4-digit Parent PIN modal + 5-min inactivity lock (client-side guard; server remains the authorization authority) | n/a |

## 6. Key Runtime Flow: Atomic Transaction (FR-P5, NFR-1)

```text
 Parent SPA            API — LedgerService           PostgreSQL
     │ POST /api/v1/transactions │                        │
     ├──────────────────────────►│ BEGIN TX               │
     │                           │ SELECT child … FOR UPDATE (row lock)
     │                           ├───────────────────────►│
     │                           │ new_balance = balance ± amount
     │                           │ if < 0 → ROLLBACK, 4xx │
     │                           │ INSERT transaction +   │
     │                           │ UPDATE current_balance │
     │                           ├───────────────────────►│ COMMIT
     │◄──────────────────────────┤ 200 OK                 │
     │                           │ SignalR → group child_<id>:
     │◄──────────────────────────┤ "OnBalanceUpdated" (parent & child
                                 │  devices refresh instantly)
```

Pessimistic row locking inside a DB transaction serializes concurrent writes from two parents and guarantees `remaining_after` accuracy. The timeline query is served by a composite index `(child_id, created_at DESC)` (NFR-3).

## 7. Deployment View

```text
 ┌───────────────┐      ┌───────────────────────┐      ┌──────────────────┐
 │ Static hosting│      │ API hosting           │      │ Managed          │
 │ + CDN         │      │ (container / app svc) │      │ PostgreSQL 16+   │
 │ Blazor WASM   ├─────►│ ASP.NET API + SignalR ├─────►│ + automated      │
 │ bundle        │      │ single instance (MVP) │      │ backups          │
 └───────────────┘      └───────────────────────┘      └──────────────────┘
        Firebase Auth and SendGrid are external SaaS — nothing to operate.
```

MVP runs a single API instance. Scaling note: horizontal scale-out later requires sticky sessions or a SignalR backplane (e.g., Redis). Hosting provider is intentionally left open at this stage.

## 8. Cross-Cutting Concerns → NFR Traceability

| Concern | Architectural response |
| :--- | :--- |
| Data integrity (NFR-1) | DB transaction + `FOR UPDATE` row lock per §6 |
| Performance (NFR-3) | Composite index `(child_id, created_at DESC)`; cached `current_balance` avoids ledger re-summing |
| Security (NFR-4) | PINs hashed (never plaintext); JWT validation middleware; IP-ban guard; household scoping on every query |
| Auditability | Append-only `audit_logs` written by all admin/security actions |
| Time handling | All timestamps stored UTC; UI renders local time (SRS §8) |

## 9. Architectural Decisions Summary

| # | Decision | Rationale |
| :--- | :--- | :--- |
| AD-1 | Monolithic API + SPA, no microservices | MVP scale (≤ 2 parents, ≤ 9 children per household); one deployable, one database |
| AD-2 | Blazor WASM + shared DTO project | Single .NET skillset end-to-end; contracts shared at compile time |
| AD-3 | Firebase Auth for parents only | Outsource password management/SSO; children stay simple with PIN login |
| AD-4 | Custom 365-day child JWT with `SecurityStamp` | Meets persistent-login requirement (FR-C2) while staying revocable on PIN reset |
| AD-5 | PostgreSQL + EF Core Code-First | ACID ledger semantics, migrations, pessimistic locking support |
| AD-6 | Append-only ledger & audit log; corrections as new transactions | Immutability = trust between parents and kids (FR-P6) |
| AD-7 | Parent PIN lock enforced client-side; authorization always server-side | Good shared-device UX without weakening the security boundary |
| AD-8 | SignalR push after commit | Child devices see balance changes instantly without polling |

## 10. Open Items

1. **Hosting provider** not yet selected — deployment view (§7) is cloud-agnostic by design.
2. **SignalR scale-out** strategy (sticky sessions vs. backplane) deferred until post-MVP load requires it.

# Pocket-Money — System Architecture Design (SAD)

> * **Project Name:** Pocket-Money, Virtual Family Allowance & Ledger Web Application
> * **Version:** 1.0
> * **Target Release:** V1 Minimal Viable Product (MVP)
> * **Date:** 13 August 2026
> * **Document Status:** Approved / Final Baseline
> * **Companion Docs:** SRS v1.0 (requirements) · SDS v1.0 (source of truth for module-level design)

## 1. Purpose & Scope

This document describes the **macro view** of Pocket-Money V1: system context, logical layers, runtime interactions, authentication model, multi-tenancy, deployment, and key architectural decisions. All module- and code-level detail — schema, constants, algorithms, API contracts, validation rules, migration strategy, testing — is normative in the **SDS**; this document defers to it.

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

A decoupled SPA + monolithic API, organized in four layers (SDS §1.3):

* **Core** — Domain + Application (+ Application.Model, Application.Contract). All business rules.
* **Infrastructure** — Persistence (EF Core / PostgreSQL) + Authentication (Firebase adapter).
* **Presentation** — Api, Client (Blazor WASM), Shared (API↔Client only).
* **Cross-layer** — Global: enums, constants, helpers.

Dependency direction: Presentation → Core ← Infrastructure; Global referenced by all.

```text
┌─────────────────────────── Browser ────────────────────────────┐
│  PRESENTATION: PocketMoney.Client (Blazor WASM)                │
│   Parent dashboard │ Child dashboard │ Parent-PIN lock guard   │
│   Inactivity timer (5 min) │ 365-day child token storage       │
└──────────┬───────────────────────────────────┬─────────────────┘
           │ REST /api/v1 (JSON over HTTPS)    │ WSS /hubs/ledger
┌──────────▼───────────────────────────────────▼─────────────────┐
│  PRESENTATION: PocketMoney.Api (ASP.NET Core 11)               │
│   Middleware pipeline: IP-ban guard → AuthN (Firebase JWT │    │
│   child JWT + security-stamp check) → household scoping →      │
│   endpoints → audit log                                        │
│   LedgerHub (SignalR): real-time balance/timeline push         │
├────────────────────────────────────────────────────────────────┤
│  CORE: PocketMoney.Application                                 │
│   LedgerService (atomic credit/debit) │ LockoutService │       │
│   AuditService │ Base31Generator │ invitation & PIN flows      │
│  PocketMoney.Domain: entities & value objects                  │
│  Application.Model / Contract: DTOs & interfaces               │
├────────────────────────────────────────────────────────────────┤
│  INFRASTRUCTURE: Persistence │ Authentication                  │
│   EF Core DbContext, configurations, migrations │ Firebase     │
└──────────────────────────────┬─────────────────────────────────┘
                               │ Npgsql
                     ┌─────────▼─────────┐
                     │   PostgreSQL 16+  │
                     └───────────────────┘

  CROSS-LAYER: PocketMoney.Global (enums, constants, helpers)
  PRESENTATION: PocketMoney.Shared (API ↔ Client only)
```

## 4. Multi-Tenancy & Data Architecture

* **Tenant boundary:** `Household`. Every tenant-scoped row carries a `household_id` foreign key.
* **Enforcement:** defense in depth — (1) the API middleware resolves the caller's household from the verified token and scopes every query; (2) EF global query filters scope all tenant entities by `household_id`; (3) child sessions are additionally restricted to their own `child_id` (read-only). PostgreSQL RLS is **out of scope for V1** (SDS §10).
* **Membership rules:** max 2 parents and max 9 children per household; one Firebase user belongs to at most one household, ever (SDS §2.4, §5).
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

* **Ledger integrity:** `transactions` is append-only; corrections are new adjustment transactions. Each row stores a `remaining_after` snapshot and a **currency snapshot** (`CurrencyKey`), so history keeps its own denomination even after a child's currency change; `children.current_balance` is a cached running total updated atomically with the insert (see §6).
* **Timeline access:** keyset (cursor) pagination — pages of 25, constant cost at any depth, `nextCursor: null` = end of history (SDS §12).

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
     │ POST /api/v1/household/transactions │                        │
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

Pessimistic row locking inside a DB transaction serializes concurrent writes from two parents and guarantees `remaining_after` accuracy. Timeline queries are served by the composite index `(child_id, created_at DESC, id DESC)` (NFR-3, SDS §2.4/§12).

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
| Performance (NFR-3) | Composite index `(child_id, created_at DESC, id DESC)`; cached `current_balance`; keyset pagination keeps timeline pages O(1) at any depth |
| Security (NFR-4) | PINs hashed (never plaintext); JWT validation middleware; IP-ban guard; household scoping on every query; input validation at UI/API/DB (SDS §9) |
| Auditability | Append-only `audit_logs` written by all admin/security actions |
| Time handling | All timestamps stored UTC; UI renders local time (SRS §8) |
| Quality | Tiered test strategy (Application unit, API integration on real PostgreSQL, Client bUnit) + immutable EF migrations — SDS §13/§11 |

## 9. Architectural Decisions Summary

| # | Decision | Rationale |
| :--- | :--- | :--- |
| AD-1 | Monolithic API + SPA, no microservices | MVP scale (≤ 2 parents, ≤ 9 children per household); one deployable, one database |
| AD-2 | Clean-architecture layering (Core / Infrastructure / Presentation / Cross-layer) | Business rules isolated in Core, testable without DB; framework concerns (EF, Firebase) isolated in Infrastructure |
| AD-3 | Firebase Auth for parents only | Outsource password management/SSO; children stay simple with PIN login |
| AD-4 | Custom 365-day child JWT with `SecurityStamp` | Meets persistent-login requirement (FR-C2) while staying revocable on PIN reset |
| AD-5 | PostgreSQL + EF Core Code-First | ACID ledger semantics, migrations, pessimistic locking support |
| AD-6 | Append-only ledger & audit log; corrections as new transactions | Immutability = trust between parents and kids (FR-P6) |
| AD-7 | Parent PIN lock enforced client-side; authorization always server-side | Good shared-device UX without weakening the security boundary |
| AD-8 | SignalR push after commit | Child devices see balance changes instantly without polling |
| AD-9 | Keyset (cursor) pagination for timelines | Constant page cost, concurrent-insert safety, clean end-of-history signal (SDS §12) |
| AD-10 | No PostgreSQL RLS in V1 | Household isolation enforced by middleware + EF global query filters; RLS may be added later as an extra layer |
| AD-11 | Per-child currency (closed `CurrencyType` set) with per-row ledger snapshots; currency change carries balance numerically, no conversion | Keeps the ledger append-only and self-describing; history renders in its original denomination (SDS §2.1.1, §2.3, §4) |

## 10. Open Items

1. **Hosting provider** not yet selected — deployment view (§7) is cloud-agnostic by design.
2. **SignalR scale-out** strategy (sticky sessions vs. backplane) deferred until post-MVP load requires it.

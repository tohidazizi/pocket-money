# API Specification

> * **Project:** Pocket-Money
> * **Version:** 1.0
> * **Date:** 13 August 2026
> * **Target Release:** V1 MVP
> * **Document Status:** Approved / Final Baseline
> * **Companion Docs:** SRS v1.0 (requirements) · SDS v1.0 (source of truth) · SAD v1.0 (architecture)
> **Scope:** REST API surface of `PocketMoney.Api` — all 14 endpoints of SDS §7.1. SignalR (`/hubs/ledger`) is specified in SDS §7.2 and is outside this document.

## 1. Conventions

### 1.1 Base URL & Versioning

* Base path: `/api/v1`. All routes below are relative to it.
* Breaking changes start `/api/v2`; additive changes stay in `v1`.

### 1.2 Authentication

| Scheme | Bearer token | Applies to |
| --- | --- | --- |
| Parent | Firebase ID token | All `/household/*` routes except `invitations/accept` and child-scoped reads |
| Firebase | Firebase ID token | `POST /household/invitations/accept` only |
| Child | Custom 365-day JWT embedding `child_id` + `security_stamp` claims (SDS §3.2) | `GET /household/children/me`, `GET /household/transactions` (own rows only) |
| None | — | `POST /auth/child/login`, static assets (IP ban never blocks static assets, SRS NFR-4) |

* Requests without a resolvable `household_id` are rejected before reaching the endpoints (SDS §10, layer 1).
* Child JWT with a stale `security_stamp` → `401` (PIN was reset; device must re-authenticate, SDS §3.2).

### 1.3 Common Rules

* **Timestamps:** UTC, ISO 8601 (`2026-08-10T14:30:00Z`). UI renders local time (SRS §8, SDS §9.5).
* **Money:** JSON numbers. `amount` ≤ 9,999,999,999.999 (Decimal(13,3)); balances `Decimal(19,3)` (SDS §2.4). Display formatting — exactly the currency's `DecimalDigits` decimal places — is a client responsibility (SDS §9.5).
* **Currency:** always exchanged as a currency **key** string (`Point`, `IRR`, `USD`, `OMR`, …). Where a response carries currency info, it is the resolved record: `{ key, symbol, country, title, nativeTitle, decimalDigits }` (SDS §2.1.1).
* **Validation:** all inputs trimmed and validated per SDS §9 before any domain logic; violations → `400`.
* **Household scoping:** every response contains only data from the caller's household. Unknown or out-of-household IDs return `404` (never `403`) to avoid leaking existence.

### 1.4 Error Model

All errors are ASP.NET Core **ProblemDetails** per RFC 9457 (SDS §7.0); statuses formally mapped in SDS §7.1.1:

```json
{
  "type": "https://pocketmoney.app/errors/negative-balance",
  "status": 422,
  "title": "Negative balance not acceptable",
  "detail": "Human-readable detail.",
  "code": "negative_balance_not_acceptable"
}
```

* `type` — stable URI reference identifying the error class.
* `status` — if present, number indicating the HTTP status code (only advisory),
* `title` — short human-readable summary. It SHOULD NOT change from occurrence to occurrence of the problem, except for localization. See: [RFC 9110, section-12.1](https://www.rfc-editor.org/info/rfc9110/#section-12.1)
* `detail` — human-readable instance-specific explanation to this occurrence of the problem.
* `code` — extension member carrying the stable machine-readable codes from the table below.
* Lockout responses extend ProblemDetails with `lockedUntil` (`LockedErrorDetails : ProblemDetails`, §2.1).

| Status | Codes used | Meaning |
| --- | --- | --- |
| `400` | `validation_error` | SDS §9 violations: length, characters, format, decimal scale, unknown currency key |
| `401` | `invalid_credentials`, `token_invalid`, `token_expired`, `security_stamp_mismatch` | Auth failures |
| `403` | `ip_banned`, `owner_only`, `invitation_sender_only` | IP ban (SDS §3.3); non-owner attempted household deletion (FR-P1); invitation cancelled by a parent other than its sender (SDS §5 step 7) |
| `404` | `not_found` | Missing resource, or resource outside caller's household |
| `409` | `parent_cap_reached`, `invitation_pending`, `already_in_household`, `invitation_invalid`, `invitation_expired`, `children_max_reached` | Invitation-flow conflicts (SDS §5); child cap (SDS §2.1) |
| `422` | `negative_balance_not_acceptable` | Business rule rejection after validation (SDS §4) |
| `423` | `account_locked`, `account_permanently_locked` | Child account lockout (SDS §2.1 Lockout, §3.3) |

## 2. Auth — Child

### 2.1 POST /auth/child/login — public

Authenticates a child (FR-C1). Every attempt — successful or not — is recorded in `login_attempts` (SDS §3.3).

Request:

| Field | Type | Rules |
| --- | --- | --- |
| `accountId` | string | 5 chars; lowercase normalized to uppercase before lookup (SDS §9.2) |
| `pin` | string | `^\d{4}$` |

`200` — success (attempt counter reset to 0):

```json
{
  "token": "***",
  "expiresAt": "2027-08-10T14:30:00Z",
  "child": { "id": "…", "accountId": "MJ74K", "displayName": "Mia" }
}
```

Token lifetime: `Constants.Child.TokenLifetimeDays` = 365 days (FR-C2). The child dashboard data (balance + currency) comes from `GET /household/children/me` (§5.4), not from the login response.

Errors:

* `401 invalid_credentials` — wrong account ID or PIN; `unsuccessful_login_attempts` incremented (never reset by logout, FR-C2).
* `423 account_locked` — ProblemDetails carries a `lockedUntil` extension (5-min / 15-min tiers).
* `423 account_permanently_locked` — 9+ cumulative failures, **no** `lockedUntil` extension; only a parent's PIN reset unlocks (SDS §7.1).
* `403 ip_banned` — IP is in an active ban (SDS §3.3 IpBan).

**No logout endpoint:** child JWTs are stateless; logout discards the token locally and does not reset the failure counter (FR-C2).

## 3. Household

### 3.1 Household creation — no endpoint

A household is auto-created server-side on a parent's **first** Firebase sign-in (SDS §7.1 footnote, §7.1.2). We call it Auto-Registration.

#### Auto-Registration

**For the first parent** = Create a new Household + create the Parent related to it.

**For the next parent** = Create the Parent related to the household that invited them.

#### Onboarding

**For the first parent** = `PUT /household/settings` (display name + default currency) + `PUT /household/parents/me/pin`.

**For the next parent** = `PUT /household/parents/me/pin`.

UI should handle the page flow based on API responses to first-timers.

### 3.2 GET /household — parent JWT

Parent landing-page payload: household info, the **`parents` array**, and **the children list with current balances** (SDS §7.1). Also carries the data driving the invite-button rule (SDS §5 step 6). Max 9 children / 2 parents → no pagination.

`200`:

```json
{
  "id": "…",
  "displayName": "The Azizi Family",
  "defaultCurrency": { "key": "USD", "symbol": "$", "country": "US", "title": "US Dollar", "nativeTitle": null, "decimalDigits": 2 },
  "maxParents": 2,
  "pendingInvitations": [
    { "id": "…", "email": "moj@example.com", "expiresAt": "2026-08-17T09:00:00Z" }
  ],
  "createdAt": "2026-08-01T10:00:00Z",
  "parents": [
    { "id": "…", "displayName": "Tohid", "isOwner": true, "hasPin": true },
    { "id": "…", "displayName": "Moj", "isOwner": false, "hasPin": true }
  ],
  "children": [
    { "id": "…", "accountId": "MJ74K", "displayName": "Mia",
      "currency": { "key": "USD", "symbol": "$", "country": "US", "title": "US Dollar", "nativeTitle": null, "decimalDigits": 2 },
      "currentBalance": 87.5, "locked": false }
  ]
}
```

* `parents` — one entry per parent (max 2). `isOwner` = the household creator (earliest `Parent.CreatedAt`, SDS §2.3); the client matches the caller's Firebase UID against `id` to learn whether *it* is the owner (drives the delete-household affordance). `hasPin` = the parent has set their Parent Lock PIN; a first-timer with `hasPin: false` is routed through onboarding (SDS §7.1.2).
* `pendingInvitation` is `null` when none exists. Client hides/disables "Invite another parent" when `parents.length >= maxParents` **or** `parents.length + pendingInvitations?.length > maxParent` — server `409`s remain the authority (SDS §5 step 6). Its `id` is the handle for cancellation (§3.7).
* `locked` is `true` for timed or permanent lockout, so the parent dashboard can offer the PIN reset (the unlock path, SDS §7.1).

### 3.3 PUT /household/settings — parent JWT

Sets household settings: display name & **default currency** (SDS §7.1; default currency = what new children inherit, FR-P1). Logged to `AuditLog` as `HouseholdSettingsUpdated` (SDS §8).

| Field | Type | Rules |
| --- | --- | --- |
| `displayName` | string? | ≤ 60; letters/digits/space `- ' .` (SDS §9.2) |
| `defaultCurrencyKey` | string | Must resolve via `CurrencyType.Parse` — unknown key → `400` (SDS §2.1.1) |

`200` — updated household (shape of §3.2). `400` validation.

Changing the household default currency affects **future** children only; existing children keep their own currency.

### 3.4 DELETE /household — parent JWT

Physical deletion of the tenant subtree; **owner parent only** (FR-P1). Irreversible; UI confirmation is client-side (SRS FR-P1).

* `204` — deleted. `audit_logs` and global `login_attempts` survive (SDS §10). Logged: `HouseholdDeleted`.
* `403 owner_only` — caller is not the household creator (owner = earliest `Parent.CreatedAt` in the household, SDS §2.3).

### 3.5 POST /household/invitations — parent JWT

Invites the 2nd parent (FR-P2, SDS §5).

| Field | Type | Rules |
| --- | --- | --- |
| `email` | string | ≤ 320, RFC-5322 shape (SDS §9.2) |

* `200` — `{ "invitationId": "…", "expiresAt": "…" }` (7-day expiry); SendGrid dispatch (SDS §1.5).
* `409 parent_cap_reached` — household already has 2 parents.
* `409 invitation_pending` — an unaccepted, unexpired invitation already exists.

Logged: `ParentInvited` (SDS §8).

### 3.6 POST /household/invitations/accept — Firebase JWT

Links the accepting parent's Firebase UID to the household (SDS §5).

| Field | Type | Rules |
| --- | --- | --- |
| `token` | string | Invitation token from the emailed link |

* `200` — `{ "householdId": "…", "displayName": "…" }`. Logged: `ParentJoined`.
* `409 invitation_invalid` / `409 invitation_expired` — bad or stale token.
* `409 parent_cap_reached` — cap re-checked **inside the acceptance transaction** (closes the two-invitations race, SDS §5 step 5).
* `409 already_in_household` — the Firebase UID already belongs to a household, including one auto-created at first sign-in (SDS §2.4 one-parent–one-household rule).

### 3.7 DELETE /household/invitations/{id} — parent JWT

Cancels a pending invitation (SDS §5 step 7) — frees the household to send a new one.

* `204` — cancelled. Logged: `ParentInvitationCancelled` (SDS §8).
* `404 not_found` — no such invitation in the caller's household (or already accepted/expired).
* `403 invitation_sender_only` — the caller is not the parent who sent it (SDS §5 step 7).

## 4. Parents

### 4.1 PUT /household/parents/me/pin — parent JWT

Sets or updates the caller's 4-digit Parent Lock PIN. Logged: `ParentPinChanged` (SDS §8).

| Field | Type | Rules |
| --- | --- | --- |
| `currentPin` | string? | `^\d{4}$` — **required when the caller already has a PIN** (`hasPin = true`, must match stored hash); **absent on the first-time set** (nothing to verify yet, SDS §2.3 sentinel, §7.1.2) |
| `newPin` | string | `^\d{4}$` |

* `200` — `{ }`.
* `401 invalid_credentials` — `currentPin` mismatch.
* `400 validation_error` — format violations (including `currentPin` supplied with `hasPin: false`, or omitted with `hasPin: true`).

## 5. Children

### 5.1 POST /household/children — parent JWT

Creates a child profile (FR-P3). Server generates the Base-31 Account ID (uniqueness-retry per SDS §3.1) and an initial random 4-digit PIN. The child **inherits `Household.DefaultCurrencyKey`** (SDS §7.1).

| Field | Type | Rules |
| --- | --- | --- |
| `displayName` | string | ≤ 100; letters/digits/space `- ' .` (SDS §9.2) |

* `201`:

```json
{ "id": "…", "accountId": "MJ74K", "displayName": "Mia", "initialPin": "4821",
  "currencyKey": "USD", "currentBalance": 0 }
```

`initialPin` is returned **only here** (shown to the parent once) — never retrievable again (SDS §7.1). Logged: `ChildCreated` (SDS §8).

* `409 children_max_reached` — household already has `Constants.Child.ChildrenMax` (9) children.

### 5.2 PUT /household/children/{id}/pin — parent JWT

Sets a new child PIN (FR-P4). Side effects (SDS §3.2, §7.1):

1. `PinHash` updated;
2. `SecurityStamp` rotated → all active 365-day child tokens rejected with `401`;
3. lockout counters reset (`unsuccessful_login_attempts = 0`, `locked_until = null`) — this is also the **parent unlock** mechanism (NFR-4).

| Field | Type | Rules |
| --- | --- | --- |
| `newPin` | string | `^\d{4}$` |

* `200` — `{ }`. Logged: `ChildPinReset` (+ `ChildAccountUnlocked` when it clears a lockout).

### 5.3 PUT /household/children/{id}/currency — parent JWT

Changes a child's currency (FR-P7, SDS §2.1.1). Semantics:

* The current balance **carries over numerically** — no conversion rate; the parent makes this decision knowingly.
* Past ledger rows are immutable and keep their snapshotted currency (SDS §4); the timeline renders each row in its own denomination (SDS §9.5).

| Field | Type | Rules |
| --- | --- | --- |
| `currencyKey` | string | Must resolve via `CurrencyType.Parse` — unknown key → `400` |

* `200` — `{ "currency": { …resolved record… }, "currentBalance": 87.5 }`. Logged: `ChildCurrencyChanged` (SDS §8).
* Then SignalR `OnBalanceUpdated` push to group `child_{id}` so the child's open dashboard re-renders in the new denomination (SDS §7.2).

### 5.4 GET /household/children/me — child JWT

Child dashboard source (FR-C3, SDS §7.1). Server scopes the child JWT to its own `child_id` (SDS §10 layer 3).

`200`:

```json
{ "displayName": "Mia", "currentBalance": 87.5,
  "currency": { "key": "USD", "symbol": "$", "country": "US", "title": "US Dollar", "nativeTitle": null, "decimalDigits": 2 } }
```

## 6. Transactions

### 6.1 POST /household/transactions — parent JWT

Atomic `CREDIT`/`DEBIT` (FR-P5, SDS §4). Concurrency: `FOR UPDATE` row lock inside a DB transaction.

| Field | Type | Rules |
| --- | --- | --- |
| `childId` | guid | Must belong to caller's household |
| `type` | string | `CREDIT` \| `DEBIT` |
| `amount` | decimal | `> 0`, ≤ 9,999,999,999.999; fractional scale ≤ the **child's currency** `DecimalDigits` — **rejected, never rounded** (SDS §9.4) |
| `reason` | string | 1–255 chars after trim; control chars stripped; emoji whitelist (SDS §9.2) |

* `200`:

```json
{
  "id": "…", "childId": "…", "type": "CREDIT", "currencyKey": "USD",
  "amount": 5.0, "reason": "Mowed Lawn",
  "remainingAfter": 87.5, "createdAt": "2026-08-10T14:30:00Z"
}
```

`currencyKey` is the ledger-row snapshot taken at insert time (SDS §4). Then SignalR `OnBalanceUpdated` push to group `child_{id}` (SDS §4, §7.2).

* `422 negative_balance_not_acceptable` — DEBIT below zero; transaction rolled back (FR-P5 step 2).
* `404 not_found` — child outside caller's household.

There are **no edit/delete endpoints** — corrections are new adjustment transactions (FR-P6).

### 6.2 GET /household/transactions — parent JWT / child JWT

Keyset-paginated timeline, newest first (FR-C4, SDS §12). **Child callers see only their own rows** (SDS §7.1 footnote); parents see the whole household, optionally filtered to one child.

Query params:

| Param | Type | Rules |
| --- | --- | --- |
| `childId` | guid | Filter to one child; ignored for child JWTs (always own rows) |
| `type` | string | `CREDIT` \| `DEBIT` |
| `from` / `to` | ISO 8601 date | Transaction date range |
| `minAmount` / `maxAmount` | decimal | Amount range |
| `q` | string | Reason search (substring, case-insensitive) |
| `cursor` | string | Opaque keyset of `(created_at, id)` of last row; omit for page 1 |
| `pageSize` | byte | Default `Timeline.DefaultPageSize` (25); clamped at `Timeline.MaxPageSize` (100); invalid → `400` (SDS §2.1) |

**Filters apply before pagination:** the cursor is computed over the filtered set; the client must resend the same filter values together with the cursor (SDS §12.2).

`200`:

```json
{
  "items": [
    { "id": "…", "childId": "…", "type": "CREDIT", "currencyKey": "USD",
      "amount": 5.0, "reason": "Mowed Lawn",
      "remainingAfter": 87.5, "createdAt": "2026-08-10T14:30:00Z" }
  ],
  "nextCursor": "…"
}
```

* `nextCursor: null` = **end of history** — clients stop fetching (SDS §12.3 stop rule).
* No total count by design (SDS §12.2).
* Each row carries its own `currencyKey` snapshot — after a currency change, history renders rows in their original denomination (SDS §9.5).

## 7. Open Decisions — Flagged for SDS Confirmation

The endpoint surface above matches SDS §7.1 exactly (all former "derived endpoints" are now first-class SDS endpoints). The following behavioral details were specified here first and have since been adopted into the SDS:

* Error status mapping (`422` negative balance, `423` lockouts, `403` IP ban) → **SDS §7.1.1**.
* Parent onboarding flow (first parent: settings + PIN; second parent: PIN only) → **SDS §7.1.2**.
* `initialPin` returned only in the `POST /household/children` response → **SDS §7.1** child-creation row.
* SignalR `OnBalanceUpdated` also fired after a currency change (§5.3) → **SDS §7.2**.
* Household owner = earliest `Parent.CreatedAt` (owner-only deletion) → **SDS §2.3**.
* Reason substring search on the timeline → **SDS §7.1 / §12.2**.
* `parents` array with `isOwner` / `hasPin` (owner detection, onboarding signal) → **SDS §7.1** GET row.
* ProblemDetails (RFC 9457) error model, `lockedUntil` extension, Minimal APIs → **SDS §7.0, §7.1.1**.
* Invitation cancellation (`DELETE /household/invitations/{id}`, sender only) → **SDS §5 step 7**.

Remaining API-level detail without an SDS statement:

* `PUT /household/parents/me/pin` request shape (`currentPin` required only when a PIN exists) — request/response contract lives here (§4.1); SDS §7.1 names the operation only.

# Pocket-Money — UI Specification

> * **Project Name:** Pocket-Money, Virtual Family Allowance & Ledger Web Application
> * **Version**: 1.0
> * **Date**: 5 August, 2026
> * **Status**: Approved
> * **Scope**: Client application (`PocketMoney.Client`)
> * **Related docs**: Software Requirement Specification (SRS), Software Design Specification, API Specification

This document captures the **UI-layer decisions** agreed between product and engineering. Where it amends the main SRS, the amendment is called out explicitly. API contracts live in the API Specification; this document never redefines them.

## 1. Technology & Design System

* **Framework:** Blazor WebAssembly (`PocketMoney.Client`, SDS §1.1/§1.3), targeting modern evergreen browsers.
* **Component library:** MudBlazor (Material Design). The prototype and the final UI are built on the same design language so prototype screens map ~1:1 to MudBlazor components (AppBar, Cards, FAB, Dialog, NavDrawer, …).
* **Design language:** Google Material Design 3 conventions:
  * Color roles (primary / surface / outline / error), tonal palettes.
  * 8 dp spacing grid; elevation levels; Nunito typeface (§1.1).
  * 48 px minimum touch targets (NFR-2 touch-friendly).
  * One shared MD3 theme for parent and child surfaces, differentiated by accent color (parent = primary, child = secondary accent) — not two separate themes.
* **Prototype form:** static HTML/CSS/JS implementing the same MD3 tokens, so every screen is reviewable in any browser without hosting.

### 1.1 Theme Palette (approved, Prototype 3)

The approved visual identity is "Ledger Paper": warm cream surfaces, deep ledger-green primary, warm amber child accent, muted brick tertiary, muted-red error. MD3 is a token system — the architecture (color roles, spacing, components) stays MudBlazor-compatible while the values are custom. `ui_prototype_v03.html` is the accepted visual reference (v01/v02 remain in the folder as history only).

| MD3 role | Value | MudBlazor `Palette` field |
| --- | --- | --- |
| Primary | `#14603F` | `Primary` |
| On Primary | `#FDFBF3` | `PrimaryDarkText` |
| Primary Container | `#DFEDDD` | `PrimaryLighten` |
| On Primary Container | `#12341F` | — |
| Secondary (child accent) | `#B26A1B` | `Secondary` |
| On Secondary | `#FFF8EE` | `SecondaryDarkText` |
| Secondary Container | `#F7E6C8` | `SecondaryLighten` |
| On Secondary Container | `#4A3007` | — |
| Tertiary | `#93493A` | `Tertiary` |
| Tertiary Container | `#F6DFD3` | `TertiaryLighten` |
| Background | `#F4F1E7` | `AppbarBackground` / `Background` |
| Surface | `#FBF9F2` | `Surface` |
| Surface Low / High | `#F8F5EC` / `#F1EDE0` | neutral row/hover tones |
| On Surface | `#26312A` | `TextPrimary` |
| On Surface Variant | `#5C6B60` | `TextSecondary` |
| Outline / Variant | `#8FA092` / `#DCE3D4` | `LinesDefault` / `LinesInputs` |
| Error / Debit | `#C23D3D` | `Error` |
| Error Container | `#FBEAEA` | `ErrorLighten` |
| Success / Credit | `#147047` | `Success` |
| Success Container | `#E7F2EC` | `SuccessLighten` |
| Lock badge | `#F7E6C8` bg / `#7A5410` ink | custom chip style |

* **Typography:** Nunito, weights 400/600/700/800 (800 for headings & emphasis).
* **Iconography:** parent surfaces use SVG/Material icons only — no emoji. Playful emoji is allowed on child surfaces and inside transaction reasons (subject to the whitelist, SDS §9.2).
* **Spacing/shape:** 8 px grid; radii 10/14/18/26 px; 48 px minimum touch targets; pill buttons (100 px radius).

## 2. Responsiveness

* **Single breakpoint:** `768 px` viewport width.
  * `< 768 px` — phones & small tablets: single column, bottom navigation, full-width cards.
  * `≥ 768 px` — large tablets, laptops, monitors: navigation rail/drawer + multi-column content (child card grid, timeline beside summary).
* Every screen must be specified at both sizes (NFR-2).

## 3. Child Login & Session Flows

### 3.1 Definitions

* **ChildrenHistory Page** — public page listing previously logged-in children, populated from ChildrenHistory Storage. Each entry shows the child's display name (and avatar placeholder) with:
  * an icon to remove that specific child from the list (local-only removal; the account itself is unaffected);
  * a "clear entire list" button with a confirmation dialog (removes all entries);
  * a button to log in as a new child (Account ID + PIN form);
  * an **"I'm a Parent"** button (parent login).
  * If ChildrenHistory Storage is unavailable or empty, the page redirects automatically to the app homepage.
* **ChildrenHistory Storage** — browser `localStorage`, key–value pairs: `{ key: AccountID, value: child_display_name }`.
* Clarification: The UI does not know if an child account is locked or not at this page. The API response to login attmept will r in with PIN **Locked children are visible but not selectable** — a locked account (timed ladder, permanent ladder, or manual lock) renders with a lock badge and, for timed locks, a live countdown. Its entry explains that a parent must unlock it (FR-P8); the PIN form is not offered.

### 3.2 Condition I — child has a dedicated device

| Scenario | Flow |
| --- | --- |
| **First visit** | Homepage → "I'm a Kid" → enter Account ID + PIN → on success, store `{ AccountID: displayName }` in ChildrenHistory Storage → child dashboard. |
| **Valid token exists** | App opens with a valid 365-day child token (FR-C2) → automatic redirect to the child dashboard. |
| **No token, history exists** | App opens without a valid token and ChildrenHistory Storage has ≥1 entry → redirect to ChildrenHistory Page → select child → PIN only → child dashboard. |

### 3.3 Condition II — child shares a device with parents

* Before handing the device to a child, the parent **logs out**. After logout the flow is identical to Condition I (parent session token is cleared).
* Child screens expose **no** "switch to parent" affordance (amends FR-S1). Reaching parent functions requires a full parent login (Firebase).

### 3.4 Single active session rule

* Parent login clears any stored child session token.
* Child login clears any stored parent session.
* ChildrenHistory Storage is retained across parent sessions (it is a convenience list, not a session).

## 4. Parent Lock PIN — scoped purpose

The Parent Lock PIN is retained **solely** as the inactivity-unlock mechanism (amends FR-S1, keeps FR-P6 inactivity behavior):

* Parent session locks after 5 minutes of inactivity (SDS §6.1; tab switching, backgrounding, and screen lock all count).
* Unlock = enter the 4-digit Parent Lock PIN in the modal.
* The PIN is **not** a gate between child and parent sessions (no switch-to-parent affordance exists — §3.3).

## 5. Account Lock & Unlock (FR-P8)

* Parents lock/unlock a child from the Parent Dashboard via `PUT /api/v1/household/children/{id}/lock` (API Spec §5.3, SDS §3.4).
* A locked child cannot log in; its PIN cannot be changed while locked (`423 account_locked` on `PUT …/pin`).
* Unlocking clears the lock, resets the failure counter, and does **not** require changing the PIN.
* UI representations:
  * Child card (parent dashboard): lock badge; "Unlock" action; for timed ladder locks a live countdown to automatic expiry.
  * Permanently locked child (ladder or manual): "Unlock" is the only recovery path (no PIN-reset shortcut).
  * Child login / ChildrenHistory: locked entries show the lock badge instead of the PIN form.

## 6. Error Surfaces (ProblemDetails)

All API errors are RFC 9457 ProblemDetails (API Spec §1.4, SDS §7.0/§7.1.1). The UI must render:

* **Timed lockouts (`423` + `lockedUntil`):** live countdown until retry.
* **Permanent lock (`423`, no `lockedUntil`):** message directing the parent to unlock.
* **IP ban (`403 ip_banned`):** dedicated screen stating the ban and its expiry tier (24 h / 1 week / 1 month).
* **Business rejections** (`422 negative_balance_not_acceptable`, `409` conflicts, `400 validation_error`, `404 not_found`) inline at the relevant form/action, using `title` + `detail`.

## 7. Prototype Scope (v1)

The prototype covers:

1. Landing page ("I'm a Kid" / "I'm a Parent") and ChildrenHistory Page.
2. Parent login (Firebase mock) + first-parent onboarding (settings + PIN) and second-parent onboarding (PIN only).
3. Parent dashboard: child cards (balance, currency, lock badge), invite-parent (send/cancel), household settings, delete household (owner only).
4. Child drill-down: per-child timeline (`GET /household/transactions?childId={id}`), log transaction, change currency (with numerical carry-over warning), lock/unlock, PIN change.
5. Child login (Account ID + PIN, and PIN-only via ChildrenHistory) and child dashboard (balance + timeline).
6. Error surfaces: lockout countdowns, permanent lock, IP-banned screen, parent PIN idle-unlock modal.
7. Empty states for all lists (no children yet, no transactions yet, no history).

## 8. Deferred to v2+

* Emoji picker for the transaction `reason` field (whitelisted set, SDS §2.1). For v1 the client strips non-whitelisted emoji pre-submit for immediate feedback; server-side stripping remains authoritative (SDS §9.2).
* Additional responsive breakpoints beyond 768 px.

## 9. Amendments to the main SRS

| Main-SRS item | Amendment |
| --- | --- |
| FR-S1 | Reworded to Session Separation: no switch-to-parent affordance; Parent PIN retained for idle-unlock only (§4). Applied in SRS v0.1. |
| FR-C2 | Logout re-entry may be PIN-only from the ChildrenHistory Page instead of re-entering the Account ID (§3). Applied in SRS v0.1. |
| FR-P4 | PIN change blocked while the account is locked; unlocking is the dedicated action (§5). Applied in SRS v0.1 as FR-P8. |

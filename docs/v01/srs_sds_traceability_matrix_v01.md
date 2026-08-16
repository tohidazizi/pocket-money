# **Pocket‑Money V1 — SRS ↔ SDS Traceability Matrix**

This matrix ensures every requirement in the **Software Requirements Specification (SRS v1.0)** is fully implemented and traceable in the **Software Design Specification (SDS v1.0)**.

**Legend:**

- **FR‑P\*** → Parent Functional Requirements  
- **FR‑C\*** → Child Functional Requirements  
- **FR‑S\*** → Shared Device / Security Requirements  
- **NFR‑\*** → Non‑Functional Requirements  
- **DS‑\*** → SDS Section Reference  

## 1. Parent Management & Onboarding (FR‑P)

| **SRS Requirement** | **SDS Implementation** |
| ------------------- | ---------------------- |
| **FR‑P1** Parent login via Firebase | DS §1.1, §1.4, §7.1 (Firebase Auth integration), §7.1.2 onboarding flow |
| Initial household setup (default currency + PIN) | DS §7.1 (`PUT /api/v1/household/settings`), DS §7.1.2 onboarding flow, DS §2.3 `Household.DefaultCurrencyKey`, DS §2.1.1 CurrencyType |
| Only first parent can delete household | DS §7.1 (`DELETE /api/v1/household`), DS §2.3 owner = earliest `Parent.CreatedAt`, DS §7.1.1 (`403 owner_only`), DS §8 Audit logging |
| Physical deletion except audit logs | DS §7.1, DS §8 (append‑only audit log) |
| **FR‑P2** Max 2 parents | DS §2.1 `MaxParentsPerHousehold = 2`, DS §5 invitation flow |
| Parent invitation flow | DS §5 (SendGrid + token + Firebase), incl. sender-only cancellation (`DELETE /invitations/{id}`) |
| Parent cannot remove other parent | DS §5 (no endpoint exists) |
| Parent cannot remove self | DS §5 (no endpoint exists) |
| **FR‑P3** Create child profiles (max 9) | DS §2.1 `ChildrenMax = 9`, DS §7.1 (`POST /api/v1/household/children`) |
| Base‑31 Account ID generation | DS §3.1 Base31Generator |
| Account ID uniqueness | DS §2.4 unique index, DS §3.1 retry logic |
| Initial random PIN | DS §7.1 child creation flow — returned once in the creation response, never retrievable again |
| New child inherits household default currency | DS §2.3 `Child.CurrencyKey`, DS §2.1.1 |
| **FR‑P4** Child PIN reset | DS §7.1 (`PUT /api/v1/household/children/{id}/pin`, `423` while locked), DS §3.2 token invalidation |
| **FR‑P8** Child account lock/unlock | DS §3.4 manual lock/unlock semantics, DS §7.1 (`PUT /api/v1/household/children/{id}/lock`), DS §8 (`ChildAccountLocked` / `ChildAccountUnlocked`) |
| **FR‑P7** Child currency change | DS §7.1 (`PUT /api/v1/household/children/{id}/currency`), DS §2.1.1, DS §4 (`CurrencyKey` snapshot per ledger row), DS §8 (`ChildCurrencyChanged`) |
| **FR‑P5** Transaction logging | DS §4 atomic transaction logic |
| Negative balance rejection | DS §4 (rollback on `< 0`), DS §7.1.1 (`422 negative_balance_not_acceptable`) |
| Append‑only ledger | DS §2.3 Transaction entity, DS §4 |
| Concurrency control | DS §4 (`FOR UPDATE` row lock) |
| **FR‑P6** Immutable ledger | DS §4 (no edit/delete), DS §7.1 (no endpoints) |
| Parent inactivity lock (5 min) | DS §2.1 `ParentInactivityLockMs`, DS §6 InactivityTimerService |

## 2. Child Interface & View (FR‑C)

| **SRS Requirement** | **SDS Implementation** |
| ------------------- | ---------------------- |
| **FR‑C1** Child login via Account ID + PIN | DS §7.1 (`POST /auth/child/login`) |
| **FR‑C2** Persistent 365‑day token | DS §2.1 `TokenLifetimeDays = 365`, DS §3.2 token validation |
| Logout invalidates token | DS §3.2 (security stamp mismatch forces re‑auth) |
| Unsuccessful login attempts not reset on logout | DS §3.3 lockout logic |
| **FR‑C3** Dashboard shows name + balance | DS §7.1 (`GET /api/v1/household/children/me`), DS §2.3 Child entity, DS §9.5 currency formatting |
| **FR‑C4** Transaction timeline sorted DESC | DS §2.4 Transaction index (DESC), DS §7.1 (`GET /api/v1/household/transactions`, incl. reason substring search), DS §12 keyset pagination |

## 3. Shared Device Guard & Security (FR‑S)

| **SRS Requirement** | **SDS Implementation** |
| ------------------- | ---------------------- |
| **FR‑S1** Session separation (no switch-to-parent affordance; Parent PIN = idle-unlock only) | DS §6.1 scope note, UI Specification §3 ChildrenHistory flow |
| **FR‑S2** Strict data isolation | DS §10 Multi-Tenant Enforcement, DS §7.2 SignalR group isolation, DS §7.1 child‑scoped endpoints |

## 4. Non‑Functional Requirements (NFR)

| **SRS Requirement** | **SDS Implementation** |
| ------------------- | ---------------------- |
| **NFR‑1** Atomicity | DS §4 EF transaction + `FOR UPDATE` |
| **NFR‑2** Responsive UI | DS §1.1 Blazor WASM, DS §6 UI behavior |
| **NFR‑3** Timeline performance | DS §2.4 composite index `(childId, createdAt, Id DESC)` |
| **NFR‑4** PIN hashing & lockout ladder | DS §2.3 `Parent.ParentPinHash` (empty = not set yet), `Child.PinHash`, DS §3.3 ladder, DS §3.4 unlock without PIN change |
| Record all unsuccessful login attempts | DS §3.3 LoginAttempt entity |
| Lockout ladder (3→5m, 6→15m, 9→permanent) | DS §2.1 Lockout constants, DS §3.3 logic, DS §7.1.1 (`423 account_locked` / `account_permanently_locked`) |
| Parent unlocks child account | DS §7.1 child PIN reset resets lockout |
| IP ban (10 failures → 24h, 1w, 1m) | DS §2.1 IpBan constants, DS §3.3 logic, DS §7.1.1 (`403 ip_banned`) |
| IP ban does not block static assets | DS §3.3 (ban applies only to auth endpoints) |

## 5. Audit Logging (SRS §7)

| **SRS Requirement** | **SDS Implementation** |
| ------------------- | ---------------------- |
| Append‑only | DS §2.3 AuditLog entity |
| No edit/delete | DS §8 AuditService |
| Log parent PIN changes | DS §8 |
| Log child PIN resets | DS §8 (`ChildPinReset`) |
| Log child currency changes | DS §8 (`ChildCurrencyChanged`) |
| Log child creation | DS §8 (`ChildCreated`) |
| Log household settings changes | DS §8 |
| Log household deletion | DS §8 |

## 6. Timezone Handling (SRS §8)

| **SRS Requirement** | **SDS Implementation** |
| ------------------- | ---------------------- |
| Store timestamps in UTC | DS §2.3 all entities use `DateTimeOffset.UtcNow` |
| UI displays local time | DS §6 (client-side responsibility) |

## 7. Input Validation (SRS §9)

| **SRS Requirement** | **SDS Implementation** |
| ------------------- | ---------------------- |
| Trim & sanitize | DS §9.1 |
| Max lengths | DS §9.2 + EF constraints |
| Allowed characters | DS §9.2 regex + whitelist |
| Decimal precision rules | DS §2.4 precision definitions, DS §9.4 |
| Trailing zeros based on currency's decimal digits | DS §9.5 Trailing-Zero Display, DS §2.1.1 `CurrencyType.DecimalDigits` |

## 8. Data Model — Currency (SRS §4.3)

| **SRS Requirement** | **SDS Implementation** |
| ------------------- | ---------------------- |
| Closed, system-defined currency set | DS §2.1.1 `CurrencyType` (sealed records + `Parse`/`TryParse`) |
| Each currency carries symbol, country, titles, decimal digits | DS §2.1.1 record properties (`Symbol`, `Country`, `Title`, `NativeTitle`, `DecimalDigits`) |
| Household default currency | DS §2.3 `Household.DefaultCurrencyKey`, DS §7.1 settings endpoint |
| Per-child currency, inherited at creation | DS §2.3 `Child.CurrencyKey`, DS §7.1 child creation |
| Transaction keeps currency snapshot | DS §2.3 `Transaction.CurrencyKey`, DS §4 atomic insert |
| Balance fields Decimal(19,3), amount Decimal(13,3) | DS §2.4 EF precision definitions, DS §9.4 |

## 9. Out‑of‑Scope Items (SRS §10)

SDS correctly excludes:

- Interest accrual  
- Savings goals  
- Notifications  
- Bank integration  
- Transaction editing  

## Final Assessment

SDS provides **full coverage** of the SRS.  
This traceability matrix shows **complete compliance** and is ready for engineering review, QA validation, and release documentation.

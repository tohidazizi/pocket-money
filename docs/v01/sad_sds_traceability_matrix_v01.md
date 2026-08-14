# SAD ↔ SDS Traceability Matrix (Version 1.0)

This matrix ensures that every architectural decision, flow, and component described in the **SAD** is fully backed by concrete design details in the **SDS**.

## 1. System Context (SAD §2)

| **SAD Element** | **SDS Implementation** |
| --------------- | ---------------------- |
| Parent uses Firebase Auth | SDS §1.1 (Technology Stack), §7.1 (Endpoints), §1.4 (Firebase service account) |
| Child uses Account ID + PIN | SDS §7.1 (`POST /auth/child/login`), §3.1 (Base31 ID), §3.3 (lockout) |
| SPA + REST + SignalR | SDS §1.1 (Blazor WASM + API + SignalR), §7.2 (LedgerHub) |
| PostgreSQL backend | SDS §1.1, §2.4 (EF configurations), §4 (atomic transactions) |
| SendGrid for invitations | SDS §5 (Parent Invitation Flow) |

✔ Fully aligned.

## 2. Logical Architecture (SAD §3)

| **SAD Layer** | **SDS Section** |
| ------------- | --------------- |
| **Presentation** (Client + API + Shared) | SDS §1.3 (Solution Structure), §6 (Shared Device Guard), §7 (API endpoints) |
| **Core** (Domain + Application + DTOs + Contracts) | SDS §1.3, §2.3 (Entities), §3 (Algorithms), §4 (LedgerService) |
| **Infrastructure** (Persistence + Authentication) | SDS §1.3, §2.4 (EF configs), §1.4 (Firebase adapter) |
| **Cross-layer** (Global constants, enums) | SDS §2.1 (Constants), §2.2 (Enums) |

✔ Perfect match.

## 3. Multi‑Tenancy & Data Architecture (SAD §4)

| **SAD Concept** | **SDS Implementation** |
| --------------- | ---------------------- |
| Household = tenant boundary | SDS §2.3 Household entity |
| All tenant rows carry `household_id` | SDS §2.3 (Parent, Child, Transaction) |
| Enforcement via middleware + EF filters | SDS §7.2 (SignalR group isolation), SDS §7.1 (household-scoped endpoints) |
| Max 2 parents | SDS §2.1 `MaxParentsPerHousehold = 2`, SDS §5 invitation flow |
| Max 9 children | SDS §2.1 `ChildrenMax = 9`, SDS §7.1 child creation |
| Append-only ledger | SDS §2.3 Transaction entity, §4 atomic insert |
| Per-row currency snapshot keeps history's denomination | SDS §2.3 `Transaction.CurrencyKey`, §4 |
| Child-scoped read endpoints (`children/me`, transactions) | SDS §7.1, §10 layer 3 |
| Keyset pagination | SDS §7.1 timeline endpoint, §12 |
| RLS out of scope | SDS §10 (Out of Scope) |

✔ Fully aligned.

## 4. Authentication & Session Architecture (SAD §5)

| **SAD Element** | **SDS Implementation** |
| --------------- | ---------------------- |
| Parent identity via Firebase | SDS §1.1, §1.4, §7.1 |
| Child identity via AccountID + PIN | SDS §7.1, §3.1, §3.3 |
| Child JWT (365 days) | SDS §2.1 `TokenLifetimeDays = 365`, §3.2 |
| SecurityStamp invalidation | SDS §3.2 |
| Lockout ladder | SDS §2.1 Lockout constants, §3.3 logic |
| IP ban ladder | SDS §2.1 IpBan constants, §3.3 logic |
| Parent PIN lock (client-side) | SDS §6 (InactivityTimerService) |

✔ Perfect match.

## 5. Atomic Transaction Flow (SAD §6)

| **SAD Flow Step** | **SDS Implementation** |
| ----------------- | ---------------------- |
| EF transaction | SDS §4 (`BeginTransactionAsync`) |
| `FOR UPDATE` row lock | SDS §4 (`SELECT ... FOR UPDATE`) |
| Negative balance rollback | SDS §4 (`newBalance < 0 → rollback`) |
| Append-only insert | SDS §2.3 Transaction entity, §4 |
| Currency snapshot per ledger row | SDS §4 (`CurrencyKey = child.CurrencyKey`) |
| Update `current_balance` | SDS §4 |
| SignalR broadcast | SDS §4 (`OnBalanceUpdated`) |

✔ Fully aligned.

## 6. Deployment View (SAD §7)

| **SAD Element** | **SDS Support** |
| --------------- | --------------- |
| SPA static hosting | SDS §1.1 (Blazor WASM) |
| API container/app service | SDS §1.1 (ASP.NET Core API) |
| PostgreSQL managed DB | SDS §1.1, §2.4 |
| External Firebase + SendGrid | SDS §1.4, §5 |

✔ No contradictions.

## 7. Cross-Cutting Concerns (SAD §8)

| **Concern** | **SDS Implementation** |
| ----------- | ---------------------- |
| Data integrity | SDS §4 atomic transaction |
| Performance | SDS §2.4 DESC index, §7.1 keyset pagination |
| Security | SDS §3.2, §3.3, §9 |
| Auditability | SDS §8 |
| Timezone | SDS §2.3 (`DateTime.UtcNow` on all entities), §9.5 (client renders local time) |
| Testing | SDS §13 |

✔ Fully aligned.

## 8. Architectural Decisions (SAD §9)

| **SAD Decision** | **SDS Evidence** |
| ---------------- | ---------------- |
| Monolithic API | SDS §1.3 |
| Clean architecture layering | SDS §1.3 |
| Firebase for parents | SDS §1.1, §7.1 |
| Custom child JWT | SDS §3.2 |
| PostgreSQL + EF Core | SDS §1.1, §2.4 |
| Append-only ledger | SDS §2.3 |
| Parent PIN lock | SDS §6 |
| SignalR push | SDS §4 |
| Keyset pagination | SDS §7.1, §12 |
| No RLS in V1 | SDS §10 |
| Per-child currency + ledger snapshots (AD-11) | SDS §2.1.1, §2.3, §4 |

✔ Perfect match.

## **Final Verdict: SAD ↔ SDS Alignment Score = 100%**

SAD and SDS are **fully consistent**, **mutually reinforcing**, and **professionally structured**.  
There are **no conflicts**, **no missing mappings**, and **no architectural gaps**.

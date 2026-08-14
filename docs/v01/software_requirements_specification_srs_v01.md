# Pocket-Money- Software Requirements Specification (SRS)

Pocket-Money app is a virtual family allowance & ledger web application.

## 1. Document Overview & Metadata

* **Project Name:** Pocket-Money, Virtual Family Allowance & Ledger Web Application
* **Version:** 1.0
* **Target Release:** V1 Minimal Viable Product (MVP)
* **Date:** 13 August 2026
* **Document Status:** Approved / Final Baseline

## 2. Project Description & Scope

### 2.1 Purpose & Goal

Pocket-Money is a multi-tenant web platform designed for parents to manage virtual financial balances for their children. The application functions like a simple digital banking system where parents act as the account administrators (depositing/subtracting funds with assigned reasons) and children act as account viewers (checking remaining balances and viewing a linear, append-only history of transaction records).

### 2.2 System Vision & Core Value

* **Simplicity First:** Eliminate complex setup for kids by replacing traditional passwords with 4-digit PINs and persistent logins.
* **Accuracy & Trust:** Immutable transaction ledger ensures both parents and kids have a single source of truth for balances and allowances.
* **Shared Device Friendliness:** Protect parent administrative controls on household-shared devices (e.g., family iPad) via a quick Parent PIN lock.

## 3. Multi-Tenancy & System Architecture

### 3.1 Multi-Tenant Isolation Model

* **Tenant Boundary:** `Household` object acts as the primary logical multi-tenant boundary.
* **Data Scoping:** All database records (`parents`, `children`, `transactions`) MUST contain a `household_id` foreign key.
* **Security Rules:** Tenant isolation enforcement MUST guarantee that users cannot read or write data outside their assigned `household_id`. The concrete enforcement mechanism is defined in the SDS.

### 3.2 Authentication & Session Architecture

* **Parent Authentication:**
  * Delegated to **Firebase Authentication**.
  * Supports Email/Password registration and Google SSO, etc..
  * Grants administrative access to household settings, child profile management, and transaction creation.
* **Child Authentication:**
  * Custom token-based login utilizing a system-generated 5-character unique username (equals to the account ID of the user) and a parent-defined 4-digit numeric PIN.
  * **Persistent Token Validity:** Child sessions issue long-lived authorization tokens valid for **365 days** (or until manual log out), preventing repeated login friction on personal child devices.
* **Shared Device Guard (Parent PIN Lock):**
  * Switching from a Child View back to the Parent Dashboard or accessing administrative routes requires passing a **4-digit Parent PIN** check.

## 4. Detailed Data Schema Specification

### 4.1 Entity Relationship Overview

The system consists of four primary entities: `Households`, `Parents`, `Children`, and `Transactions`.

```plain-text
[Household] 1 ──── ∞ [Parent Users]
    │                     
    ├───────────── ∞ [Child Profiles] (Max 9 per Household)
    │                     │
    └───────────── ∞ [Transactions] ◄──── (Linked to Child Profile)
```

### 4.2 Field Definitions

#### 1. `households` Table / Collection

| Field | Data Type | Key / Constraint | Description |
| :--- | :--- | :--- | :--- |
| `id` | UUID / String | Primary Key | Unique household identifier |
| `display_name` | String | Optional | Household/Family name |
| `default_currency_key` | String | Required | Key of the default currency (§4.3); inherited by new child profiles |
| `created_at` | Timestamp | Auto / UTC | Household creation date and time |

#### 2. `parents` Table / Collection

| Field | Data Type | Key / Constraint | Description |
| :--- | :--- | :--- | :--- |
| `id` | String | Primary Key | Firebase User UID |
| `household_id` | UUID / String | Foreign Key (`households.id`) | Multi-tenant linking identifier |
| `display_name` | String | optional | Parent name. Show 'Parent' if empty. |
| `email` | String | Unique, Required | Parent registration email |
| `parent_pin_hash` | String | Required | Bcrypt/Argon2 hash of the 4-digit Parent Lock PIN |
| `created_at` | Timestamp | Auto / UTC | Parent profile creation date |

#### 3. `children` Table / Collection

| Field | Data Type | Key / Constraint | Description |
| :--- | :--- | :--- | :--- |
| `id` | UUID / String | Primary Key | Unique child ID |
| `account_id` | string | Unique | Unique account ID, a random app PocketMoney Account ID |
| `household_id` | UUID / String | Foreign Key (`households.id`) | Multi-tenant boundary verification |
| `display_name` | String | Required | Child's display name or nickname |
| `pin_hash` | String | Required | Encrypted 4-digit PIN set and managed by parent |
| `currency_key` | String | Required | Key of the child's currency (§4.3); inherits household default at creation; changeable by parents (FR-P7) |
| `current_balance` | Decimal (19,3) | Default `0.000` | Cached running total balance, denominated in the child's currency |
| `created_at` | Timestamp | Auto / UTC | Child account creation timestamp |
| `creator` | UUID / String | Foreign Key (`parents.id`) | Creator of this child profile |
| `unsuccessful_login_attempts` | byte | default 0 | increment with each failed login; reset to 0 with successful login |
| `locked_until` | timestamp | nullable | null = not locked; reset to null if value is less than current time |

#### 4. `transactions` Table / Collection (Append-Only Ledger)

| Field | Data Type | Key / Constraint | Description |
| :--- | :--- | :--- | :--- |
| `id` | UUID / String | Primary Key | Unique transaction record ID |
| `household_id` | UUID / String | Foreign Key (`households.id`) | Strict multi-tenant data filter |
| `child_id` | UUID / String | Foreign Key (`children.id`) | Associated child profile |
| `type` | Enum | `CREDIT` \| `DEBIT` | Transaction direction (`CREDIT` = deposit, `DEBIT` = withdrawal) |
| `currency_key` | String | Required | Snapshot of the child's currency at creation time (§4.3) — history keeps its denomination |
| `amount` | Decimal (13,3) | Greater than 0.000 | Monetary amount of the transaction |
| `reason` | String (255) | Required | Descriptive memo/reason (e.g., "Mowed Lawn", "Bought Game") |
| `remaining_after` | Decimal (19,3) | Required | Snapshot of child's balance immediately following this transaction |
| `created_at` | Timestamp | Auto / UTC | Timestamp when the transaction was logged |
| `creator` | UUID / String | Foreign Key (`parents.id`) | creator of this transaction |

#### 5. `loginattempts` Table / Collection (Append-Only Ledger)

| Field | Data Type | Key / Constraint | Description |
| :--- | :--- | :--- | :--- |
| `id` | UUID / String | Primary Key | Unique transaction record ID |
| `account_id` | string | required | exactly as received from the client, even if invalid |
| `created_at` | Timestamp | Auto / UTC | Timestamp when the transaction was logged |
| `ip` | String | required | ip of the requester |
| `client_info` | JSON | required | client info extracted from the http request |

\* loginattempts is global, not per household.

### 4.3 Currency Model

* Currency is a **closed, system-defined set** (`CurrencyType`) — not free text. Each currency carries its display symbol, country, display titles, and its own number of decimal digits (e.g., Points and Iranian Rial = 0, USD = 2, Omani Rials = 3).
* A household defines a **default currency**; each child profile has its own currency, inherited from the household default at creation (FR-P3).
* Parents may change a child's currency at any time (FR-P7).
* The concrete currency list and its technical representation are defined in the SDS.

## 5. Functional Requirements (FR)

### 5.1 Parent Management & Onboarding

* **FR-P1 (Account Setup):**
  * System shall allow a parent to register/login via Firebase Auth (Email/Password or Google SSO, etc.).
  * System shall prompt initial parent setup to set the Household **default currency** (§4.3) (default value = Point) and establish a 4-digit Parent Lock PIN.
  * Only the first parent (household creator) can delete the household.
    * Household deletion shall physically delete all household data (except audit logs) and is irreversible. User must be alarmed and approve the process.
* **FR-P2 (Parent Management)** (features for version 1):
  * A parent can invite another parent to his/her Household.
  * Maximum parents in each Household: 2.
  * A parent cannot remove the other parent from the Household.
  * A parent cannot remove him/herself from a Household.
* **FR-P3 (Child Profile Creation):**
  * Parent shall be able to create child profiles (up to a maximum of **9 children** per household).
  * System shall automatically generate a unique, PocketMoney Account ID for each child profile.
    * **PocketMoney Account ID**:
      * A 5‑character string encoded in a custom base‑31 alphabet;
      * Allowed characters in account ID: "0123456789ABCDEFGHJKLMNPRTVWXYZ" (Only capital alphabet; O, I, S, U, and Q are excluded alphabets).
        * Account IDs shall always be stored, retrieved and shown in capital letters.
    * PocketMoney Account ID must be unique, if it is not, try again to generate a new random unique Account ID.
  * Child Username is his/her PocketMoney Account ID.
  * System shall set an initial random 4-digit numeric PIN for the child profile.
  * New child profiles inherit the household's default currency (§4.3).
* **FR-P4 (Child PIN Reset):**
  * Parent shall be able to view child usernames and update any child’s 4-digit PIN from the Parent Dashboard at any time.
* **FR-P7 (Child Currency Change):**
  * Parent shall be able to change a child's currency at any time from the Parent Dashboard.
  * On change, the child's current balance carries over **numerically** into the new currency (no conversion rate); the parent makes this decision knowingly.
  * Past transactions are immutable: each keeps the currency it was recorded in (§4.3, FR-P6), and the timeline renders each record in its own currency.
* **FR-P5 (Transaction Logging):**
  * Parent shall select a child, specify transaction type (`CREDIT` or `DEBIT`), enter a positive monetary `amount`, and enter a non-empty `reason`.
  * System shall perform an atomic database operation:
    1. Calculate `remaining_after`:
       * If `CREDIT`: `new_balance = current_balance + amount`
       * If `DEBIT`: `new_balance = current_balance - amount`
    2. if remaining_after is less than zero, terminate the process, rollback, and show that negative balance is not acceptable.
    3. Insert a new row into `transactions` storing `amount`, `type`, `reason`, `created_at`, and `remaining_after`.
    4. Update `children.current_balance` to `new_balance`.
    5. Concurrency must be checked. Technical details will be written in Software Design Specification (SDS).
* **FR-P6 (Immutable Ledger Guarantee):**
  * System shall NOT provide edit or delete endpoints for past transactions.
  * Error corrections must be logged as new adjustment transactions (e.g., +$5.00 "Correction for duplicate charge").
* Parent session will lock if the page is inactive for 5 minutes; to unlock, parent needs to enter the 4-digit pin.
  * Does switching browser tabs count as inactivity? Yes
  * Does backgrounding a mobile app count as inactivity? Yes
  * Does screen lock count as inactivity? Yes

### 5.2 Child Interface & View

* **FR-C1 (Child Authentication):**
  * Child shall log in using their assigned 5-character username (PocketMoney Account ID) and 4-digit PIN on a dedicated child login page.
* **FR-C2 (Persistent Child Session):**
  * System shall issue a persistent auth token stored locally on the client device valid for **365 days**.
  * Re-opening the web application on an authenticated child device shall directly open the child's home dashboard without requesting re-entry of the PIN.
  * Child can manually logout from the account.
    * Does logout invalidate the persistent token? Yes.
    * Does logout require Pocket-Money Account ID next time? Yes.
    * Does logout require PIN next time? Yes.
    * Does logout reset unsuccessful login attempts? No.
* **FR-C3 (Dashboard & Balance Summary):**
  * Child home screen shall display the child's `display_name` and `current_balance` formatted with the child's currency symbol; the number of decimal places equals that currency's decimal digits (§4.3).
* **FR-C4 (Transaction Timeline):**
  * Child home screen shall render a chronological list of all transaction records associated with the child, sorted by `created_at` descending (newest first).
  * Each record card in the timeline must explicitly present:
    * Date and Time of transaction
    * Reason / Description text
    * Transaction Type & Amount (with clear visual indicators, e.g., green `+$10.00` for credit, red `-$5.00` for debit)
    * `remaining_after` balance snapshot (e.g., "Balance after: $25.00")

### 5.3 Shared Device Guard & Security Controls

* **FR-S1 (Parent Lock Modal):**
  * Tapping "Switch to Parent Dashboard" or attempting to navigate to parent-only UI routes from a child device session must trigger a modal prompt requesting the 4-digit Parent PIN.
  * Access to administrative functions shall be denied until the valid Parent PIN is verified.
* **FR-S2 (Strict Data Isolation):**
  * Authenticated child sessions shall have read-only access restricted strictly to their own `child_id` and corresponding `transactions` records within their `household_id`.

## 6. Non-Functional Requirements (NFR)

* **NFR-1 (Data Integrity):** Transaction insertion and child balance updates MUST occur within an atomic database transaction to guarantee `remaining_after` accuracy.
* **NFR-2 (Usability & Responsiveness):** Both Parent and Child UI must be touch-friendly, high-contrast, and responsive across mobile, tablet, and desktop viewports.
* **NFR-3 (Performance):** Transaction queries for the child timeline must utilize composite indexes on `(child_id, created_at DESC)` to maintain response times < 200ms for up to 10,000 transaction rows per child.
* **NFR-4 (Security):** Child PINs and Parent Lock PINs must never be stored in plain text. Passwords/PINs must be hashed securely on server/backend functions before persistence.
* All unsuccessful login attempts to a child account must be recorded.
  * If 3 consecutive unsuccessful login attempts are made to the same child account, the account must be locked for 5 minutes;
  * After the account is unlocked, if another 3 consecutive unsuccessful login attempts are made to the same child account, the account must be locked for 15 minutes;
  * After the second unlock, if a third set of 3 consecutive unsuccessful login attempts is made to the same child account, the account must be permanently locked;
  * A parent can unlock their child’s account.
  * If 10 unsuccessful login attempts are made from the same IP address, whether against one or multiple child accounts, that IP address must be banned for 24 hours the first time, 1 week the second time, and 1 month the third time.
    * IP ban will be on the IP across the app; The IP cannot access any Parent account or PocketMoney Account in any Household.
    * IP ban does NOT block static assets, like homepage of the website, CDN, etc.

## 7. Audit important events

The following events must be logged for future auditing:

* Audit logs must be append-only
* Audit logs cannot be edited or deleted
* Parent PIN changes
* Child PIN resets
* Child currency changes
* Child profile creation
* Household settings changes
* Audit log schema will be defined in SDS document.

## 8. Timezone Handling

* Timestamps must be stored and retrieved as UTC in the database.
* The UI must display local time, when it receives time from backend.
* Household does not have timezone setting in this version.

## 9. Input Validation

* Fields cannot accept over maximum length.
* String values must be trimmed and sanitized.
* Allowed characters for each field must be checked.
* Decimal precision enforcement rules.
* Trailing zeros are displayed based on the child's currency decimal digits (§4.3).

## 10. Version 1 Out of Scope (Future Roadmap)

* Interest accrual / automatic recurring allowance payouts.
* Child savings goals or target wishlist items.
* Push notifications or email transaction alerts.
* Direct bank integration or real-money transfers.
* Transaction editing or soft deletion.

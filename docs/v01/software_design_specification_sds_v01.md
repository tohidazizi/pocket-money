# Software Design Specification (SDS)

Pocket-Money Web Application — Version 1.0

## 1. System Architecture & Project Structure

The Pocket-Money platform is designed as a decoupled, multi-tenant web application using an **EF Core Code-First** architecture with .NET 11.

### 1.1 Technology Stack Summary

* **Frontend:** Blazor WebAssembly (.NET 11)
* **Backend:** ASP.NET 11 Web API & SignalR Hubs
* **Database:** PostgreSQL 16+ via Entity Framework Core 11 (Npgsql)
* **Authentication:** Firebase Authentication (Parents) + Custom 365-day JWT Issuer (Children)
* **Email Service:** SendGrid (for second parent invitation delivery)

### 1.2 Solution Structure (`PocketMoney.sln`)

```text
├── src/
│   ├── CrossLayer /
│   │    └── PocketMoney.Global/          # Globally shared Enums, Constants, Helpers, etc.
│   │
│   ├── Domain /
│   │    └── PocketMoney.Domain/           # Core Entities & Value Objects (Code-First)
│   │        ├── Entities/
│   │        ├── ValueObjects/
│   │        │ etc.
│   │
│   ├── Application /
│   │    ├── PocketMoney.Application/      # Core Entities & Value Objects (Code-First)
│   │    │   ├── Households/
│   │    │   ├── Parents/
│   │    │   ├── Children/
│   │    │   ├── Transactions/
│   │    │   │ etc.
│   │    │
│   │    ├── PocketMoney.Application.Model/ # DTOs for Application Interfaces, API tier, and Client
│   │    │   ├── Households/
│   │    │   ├── Parents/
│   │    │   ├── Children/
│   │    │   ├── Transactions/
│   │    │   │ etc.
│   │    │
│   │    └── PocketMoney.Application.Contract/  # Application Interfaces for API tier
│   │        ├── Interfaces/
│   │        ├── Dtos/
│   │        │ etc.
│   │
│   ├── Infrastructure /   
│   │   ├── PocketMoney.Persistence/       # DbContext, EF Configurations, Repositories, Services
│   │   │   ├── Data/
│   │   │   ├── EntityConfigurations/
│   │   │   └── Migrations/
│   │   │
│   │   └── PocketMoney.Authentication/    # Firebase integration
│   │       └── adapter to Firebase
│   │
│   └── Presentation /
│       ├── PocketMoney.Shared/            # Shared "only" between API and Client
│       │   └── Utilities/
│       │
│       ├── PocketMoney.Api/               # Controllers, SignalR Hubs, Middlewares, Auth Handlers
│       │   ├── Controllers/
│       │   ├── Hubs/
│       │   └── Middlewares/
│       │
|       └── PocketMoney.Client/            # Blazor WASM UI Components, State, Services
│           ├── Pages/
│           ├── Shared/
│           └── Services/
│
```

### 1.3 Configuration & Secrets

The API reads all environment-specific values from configuration providers (environment variables / secret store). **Nothing environment-specific is hardcoded or committed.**

| Setting | Used by | Notes |
| --- | --- | --- |
| PostgreSQL connection string | EF Core (`Infrastructure`) | Per environment; pooled Npgsql |
| Firebase service account key | API middleware | Verifies parent ID tokens (server-to-server credential) |
| Child JWT signing key | Custom 365-day token issuer | Never exposed to the client |
| SendGrid API key | Invitation emails | Server-side only |
| Allowed CORS origins | API | Per-environment SPA origins |

Rules:

* Secrets live in the host's secret store / environment variables; `.env` files and connection strings are git-ignored.
* Each environment (dev / staging / prod) gets its own database and its own secret set. Hosting provider is undecided (ADD §10), so the concrete mechanism is deferred — the contract above is what the code depends on.

## 2. Domain Models & EF Core Code-First Schema

### 2.1 Shared Constants (`PocketMoney.Shared/Consts`)

```csharp
namespace PocketMoney.Shared.Consts;

public static class Constants
{
    // Account IDs (FR-P3): Base-31 alphabet, O I S U Q excluded
    public const string Base31Alphabet = "0123456789ABCDEFGHJKLMNPRTVWXYZ";

    // Shared device guard (FR-P6): parent inactivity lock, milliseconds (5 minutes)
    public const int ParentInactivityLockMs = 5 * 60 * 1000;

    // Household limits (FR-P2)
    public const byte MaxParentsPerHousehold = 2;

    public static class Child
    {
        public const byte AccountIdLength = 5;
        public const byte ChildrenMax = 9;
        public const int DisplayNameMaxLength = 100;

        // Persistent child session lifetime in days (FR-C2)
        public const ushort TokenLifetimeDays = 365;
    }

    public static class Transaction
    {
        public const int ReasonMaxLength = 255;

        // Family-friendly emoji whitelist for Transaction.Reason (SRS §9).
        // Emoji characters outside this list are stripped at the API boundary.
        // An entry implicitly includes its U+FE0F variation-selector form.
        public const string ReasonEmojiWhitelist = "😀😄😁😆🙂😉😊😍🥰😘😜😎🤩🥳😅😂🤣☺️👍👏🙌👋🤝💪🙏❤️🧡💛💚💙💜🤍🎉🎊🎁🎈⭐✨🏆🥇🏅💰💵💶💷💸🪙🌈☀️🌸🌻🌳🌙🐶🐱🐰🐼🦄🐢🦋🐝🍎🍌🍪🧁🎂🍕🍦🍿⚽🚲🎨🎮📚✏️🧩⏰";
    }

    // Child account lockout ladder (NFR-4). Tiers of MaxFailedAttemptsPerLockout
    // cumulative failures; the counter resets to 0 on a successful login.
    public static class Lockout
    {
        public const byte MaxFailedAttemptsPerLockout = 3;
        public const byte FirstLockoutMinutes = 5;    // at 3 cumulative failures
        public const byte SecondLockoutMinutes = 15;  // at 6 cumulative failures
        public const byte PermanentLockThreshold = MaxFailedAttemptsPerLockout * 3; // 9 → permanent
    }

    // Global IP ban (NFR-4). IP bans apply app-wide; static assets/CDN are exempt.
    public static class IpBan
    {
        public const byte FailureThreshold = 10;   // failures from one IP within the window
        public const byte FailureWindowHours = 24;
        public const byte FirstBanDays = 1;        // 24 hours
        public const byte SecondBanDays = 7;       // 1 week
        public const byte ThirdBanDays = 30;       // 1 month
    }
}
```

### 2.2 Shared Enums (`PocketMoney.Shared/Enums`)

```csharp
namespace PocketMoney.Shared.Enums;

public enum TransactionType
{
    Credit = 1,
    Debit = 2
}

public enum ActorType
{
    Parent = 1,
    Child = 2,
    System = 3
}

public enum AuditEventType
{
    ChildCreated,
    ChildPinReset,
    ChildAccountUnlocked,
    ParentPinChanged,
    HouseholdSettingsUpdated,
    HouseholdDeleted,
    ParentInvited,
    ParentJoined
}

```

### 2.3 Domain Entities (`PocketMoney.Domain/Entities`)

```csharp
namespace PocketMoney.Domain.Entities;

// 1. Household
public sealed class Household
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? DisplayName { get; set; }
    public string CurrencySymbol { get; set; } = "$";
    public byte DecimalDigits { get; set; } = 2; // Allowed: 0, 1, 2, 3
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public ICollection<Parent> Parents { get; set; } = new List<Parent>();
    public ICollection<Child> Children { get; set; } = new List<Child>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<HouseholdInvitation> Invitations { get; set; } = new List<HouseholdInvitation>();
}

// 2. Parent
public sealed class Parent
{
    public string Id { get; set; } = string.Empty; // Firebase User UID
    public Guid HouseholdId { get; set; }
    public string? DisplayName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string ParentPinHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Household Household { get; set; } = null!;
}

// 3. Child
public sealed class Child
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccountId { get; set; } = string.Empty; // 5-char Base31 string
    public Guid HouseholdId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; } = 0.000m;
    public string CreatorId { get; set; } = string.Empty;
    public byte UnsuccessfulLoginAttempts { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }
    public bool IsPermanentlyLocked => LockedUntil == DateTime.MaxValue;
    
    // Security Stamp changes on PIN reset to invalidate active 365-day tokens
    public Guid SecurityStamp { get; set; } = Guid.NewGuid(); 
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Household Household { get; set; } = null!;
    public Parent Creator { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}

// 4. Transaction (Append-Only)
public sealed class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public Guid ChildId { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal RemainingAfter { get; set; }
    public string CreatorId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Household Household { get; set; } = null!;
    public Child Child { get; set; } = null!;
    public Parent Creator { get; set; } = null!;
}

// 5. Global Login Attempt (Append-Only)
public sealed class LoginAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccountId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string HttpRequestInfo { get; set; } = string.Empty;
    public bool IsSuccessful { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// 6. Global IP Ban
public sealed class IpBan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IpAddress { get; set; } = string.Empty;
    public int BanCount { get; set; } = 1;
    public DateTime BannedUntil { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

// 7. Household Invitation (Parent 2 Flow)
public sealed class HouseholdInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public string InvitedEmail { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string InvitedByParentId { get; set; } = string.Empty;
    public bool IsAccepted { get; set; } = false;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Household Household { get; set; } = null!;
    public Parent InvitedByParent { get; set; } = null!;
}

// 8. Audit Log (Append-Only)
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? HouseholdId { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public ActorType ActorType { get; set; }
    public AuditEventType EventType { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

```

### 2.4 EF Core Configuration Mappings (`PocketMoney.Infrastructure/EntityConfigurations`)

```csharp
namespace PocketMoney.Infrastructure.EntityConfigurations;

public sealed class ChildConfiguration : IEntityTypeConfiguration<Child>
{
    public void Configure(EntityTypeBuilder<Child> builder)
    {
        builder.ToTable("children");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.AccountId)
            .HasMaxLength(Constants.Child.AccountIdLength)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(c => c.AccountId).IsUnique();

        // Note: SRS specifies current_balance as Decimal(10,3). (13,3) is a
        // deliberate, documented deviation: balance is a running sum of (10,3)
        // amounts and can legitimately exceed a (10,3) range; (13,3) is a strict
        // superset and keeps remaining_after and current_balance at equal width.
        builder.Property(c => c.CurrentBalance)
            .HasPrecision(13, 3)
            .HasDefaultValue(0.000m);

        builder.Property(c => c.DisplayName)
            .HasMaxLength(Constants.Child.DisplayNameMaxLength)
            .IsRequired();

        builder.HasOne(c => c.Creator)
            .WithMany()
            .HasForeignKey(c => c.CreatorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Amount)
            .HasPrecision(10, 3)
            .IsRequired();

        builder.Property(t => t.RemainingAfter)
            .HasPrecision(13, 3)
            .IsRequired();

        builder.Property(t => t.Reason)
            .HasMaxLength(Constants.Transaction.ReasonMaxLength)
            .IsRequired();

        // Optimized query performance for child timeline (FR-C4)
        builder.HasIndex(t => new { t.ChildId, t.CreatedAt });
    }
}

```

## 3. Core Algorithms & Security Engine

### 3.1 Base-31 Account ID Generator (`PocketMoney.Shared/Utilities/Base31Generator.cs`)

Excludes `O`, `I`, `S`, `U`, and `Q` to prevent visual confusion.

```csharp
public static class Base31Generator
{
    private static readonly byte AccountIdLength = Constants.Child.AccountIdLength;
    private static readonly string Alphabet = Constants.Base31Alphabet;

    public static string GenerateAccountId()
    {
        Span<byte> randomBytes = stackalloc byte[AccountIdLength];
        RandomNumberGenerator.Fill(randomBytes);
        
        Span<char> accountId = stackalloc char[AccountIdLength];
        for (int i = 0; i < AccountIdLength; i++)
        {
            accountId[i] = Alphabet[randomBytes[i] % Alphabet.Length];
        }

        return new string(accountId);
    }
}

```

### 3.2 Child Auth Token Invalidation Mechanism

When a parent updates a child's PIN (FR-P4):

1. The backend updates `Child.PinHash` and generates a new `Child.SecurityStamp = Guid.NewGuid()`.
2. Active 365-day child JWTs embed the claim `security_stamp`.
3. The API JWT Validation middleware checks the token's `security_stamp` against the database/cache record for that `ChildId`. If mismatched, it throws HTTP 401 Unauthorized, forcing the child device to re-authenticate with the new PIN.

### 3.3 Account Lockout & Global IP Ban Logic

```csharp
public async Task HandleFailedLoginAsync(string accountId, ClientInfo clientInfo, Child? child)
{
    // 1. Log attempt (accountId stored verbatim, even if invalid — audit requirement)
    _dbContext.LoginAttempts.Add(new LoginAttempt
    {
        AccountId = accountId,
        IpAddress = clientInfo.IpAddress,
        HttpRequestInfo = clientInfo.HttpRequestInfo,
        IsSuccessful = false
    });

    // 2. Check Global IP Ban threshold (NFR-4): IpBan.FailureThreshold failures
    //    from one IP within IpBan.FailureWindowHours, across any child account
    var windowStart = DateTime.UtcNow.AddHours(-Constants.IpBan.FailureWindowHours);
    var ipFailures = await _dbContext.LoginAttempts
        .CountAsync(l => l.IpAddress == clientInfo.IpAddress && !l.IsSuccessful && l.CreatedAt >= windowStart);

    if (ipFailures >= Constants.IpBan.FailureThreshold)
    {
        var existingBan = await _dbContext.IpBans.FirstOrDefaultAsync(b => b.IpAddress == clientInfo.IpAddress);
        int banCount = (existingBan?.BanCount ?? 0) + 1;

        DateTime bannedUntil = banCount switch
        {
            1 => DateTime.UtcNow.AddDays(Constants.IpBan.FirstBanDays),   // 24 hours
            2 => DateTime.UtcNow.AddDays(Constants.IpBan.SecondBanDays),  // 1 week
            _ => DateTime.UtcNow.AddDays(Constants.IpBan.ThirdBanDays)    // 1 month
        };

        if (existingBan != null)
        {
            existingBan.BanCount = banCount;
            existingBan.BannedUntil = bannedUntil;
            existingBan.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _dbContext.IpBans.Add(new IpBan { IpAddress = clientInfo.IpAddress, BanCount = banCount, BannedUntil = bannedUntil });
        }
    }

    // 3. Child-specific lockout ladder (NFR-4): 3 failures → 5 min, 6 → 15 min, 9 → permanent
    if (child != null)
    {
        child.UnsuccessfulLoginAttempts++;

        if (child.UnsuccessfulLoginAttempts >= Constants.Lockout.PermanentLockThreshold)
        {
            child.LockedUntil = DateTime.MaxValue; // IsPermanentlyLocked derives from this
        }
        else if (child.UnsuccessfulLoginAttempts == Constants.Lockout.MaxFailedAttemptsPerLockout * 2)
        {
            child.LockedUntil = DateTime.UtcNow.AddMinutes(Constants.Lockout.SecondLockoutMinutes);
        }
        else if (child.UnsuccessfulLoginAttempts == Constants.Lockout.MaxFailedAttemptsPerLockout)
        {
            child.LockedUntil = DateTime.UtcNow.AddMinutes(Constants.Lockout.FirstLockoutMinutes);
        }
    }

    await _dbContext.SaveChangesAsync();
}

```

## 4. Concurrency Control & Atomic Transactions

To prevent balance race conditions (e.g., both parents submitting a withdrawal for the same child simultaneously), the backend uses **PostgreSQL Row-Level Locking (`FOR UPDATE`)** inside an EF Core transaction.

```csharp
public async Task<TransactionResultDto> CreateTransactionAsync(CreateTransactionCommand cmd)
{
    using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    try
    {
        // Execute explicit pessimistic row lock on child entity
        var child = await _dbContext.Children
            .FromSqlRaw("SELECT * FROM children WHERE id = {0} FOR UPDATE", cmd.ChildId)
            .SingleOrDefaultAsync();

        if (child == null) return TransactionResultDto.Failed("Child account not found.");

        decimal newBalance = cmd.Type == TransactionType.Credit 
            ? child.CurrentBalance + cmd.Amount 
            : child.CurrentBalance - cmd.Amount;

        // Balance validation check (FR-P5)
        if (newBalance < 0)
        {
            await tx.RollbackAsync();
            return TransactionResultDto.Failed("Negative balance is not acceptable.");
        }

        child.CurrentBalance = newBalance;

        var transaction = new Transaction
        {
            HouseholdId = child.HouseholdId,
            ChildId = child.Id,
            Type = cmd.Type,
            Amount = cmd.Amount,
            Reason = cmd.Reason,
            RemainingAfter = newBalance,
            CreatorId = cmd.ParentId
        };

        _dbContext.Transactions.Add(transaction);
        await _dbContext.SaveChangesAsync();
        await tx.CommitAsync();

        // Broadcast real-time update via SignalR
        await _hubContext.Clients.Group($"child_{child.Id}")
            .SendAsync("OnBalanceUpdated", newBalance, transaction);

        return TransactionResultDto.Success(transaction);
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
}

```

## 5. Parent Invitation Flow (SendGrid + Firebase)

1. **Invite Request:** Parent 1 submits Parent 2’s email via `POST /api/v1/households/invite`.
2. **Token Generation:** Backend verifies Household parent count < 2, creates a `HouseholdInvitation` record with an encrypted token, and dispatches an invitation email using SendGrid.
3. **Acceptance:** Parent 2 clicks link (`[https://pocketmoney.app/accept-invite?token=](https://pocketmoney.app/accept-invite?token=)...`).
4. **Auth Link:** Parent 2 logs in or registers via Firebase Auth on Blazor WASM.
5. **Linking:** Backend validates invitation token, links Parent 2's Firebase UID to the existing `HouseholdId`, and logs the event in `AuditLog`.

## 6. Frontend State & Shared Device Guard (Blazor WASM)

### 6.1 Inactivity Lock Timer (`PocketMoney.Client/Services/InactivityTimerService.cs`)

Parent PIN lock (FR-P6) is strictly a client-side route guard.

```csharp
public class InactivityTimerService : IDisposable
{
    private Timer? _timer;
    private const int InactivityTimeoutMs = Constants.ParentInactivityLockMs; // 5 Minutes
    public event Action? OnInactivityTimeout;

    public void Start() => ResetTimer();

    public void ResetTimer()
    {
        _timer?.Dispose();
        _timer = new Timer(_ => OnInactivityTimeout?.Invoke(), null, InactivityTimeoutMs, Timeout.Infinite);
    }

    public void Dispose() => _timer?.Dispose();
}

```

* User interactions (clicks, keypresses, tab switching, window visibility changes) invoke `ResetTimer()`.
* When `OnInactivityTimeout` fires, the app updates local state to `IsParentLocked = true` and displays the **Parent PIN Unlock Modal**.

## 7. API Endpoints & SignalR Specifications

### 7.1 REST Controllers

| Method | Endpoint | Authorization | Description |
| --- | --- | --- | --- |
| `POST` | `/api/v1/auth/child/login` | Public | Authenticates child via Base31 Account ID + PIN |
| ~~`POST`~~ | ~~`/api/v1/households`~~ | ~~Firebase JWT~~ | Parent very first time log-in triggers creating a household for him/her |
| `PUT` | `/api/v1/households/{id}/settings` | Parent JWT | Sets household settings: currency & decimal accuracy |
| `DELETE` | `/api/v1/households` | Parent JWT | Physical deletion of household (Owner parent only) |
| `POST` | `/api/v1/households/invite` | Parent JWT | Generates SendGrid email invitation for 2nd parent |
| `POST` | `/api/v1/households/accept-invite` | Firebase JWT | Links 2nd parent to household |
| `POST` | `/api/v1/children` | Parent JWT | Creates child profile & generates Base31 Account ID |
| `PUT` | `/api/v1/children/{id}/pin` | Parent JWT | Updates child PIN, resets lockout, invalidates child tokens |
| `PUT` | `/api/v1/parents/me/pin` | Parent JWT | Updates a parent PIN |
| `POST` | `/api/v1/transactions` | Parent JWT | Executes atomic transaction (`CREDIT`/`DEBIT`) |
| `GET` | `/api/v1/transactions/child/{id}` | Parent / Child | Returns timeline sorted `created_at DESC` |

### 7.2 SignalR Hub (`/hubs/ledger`)

```csharp
[Authorize]
public class LedgerHub : Hub
{
    public async Task JoinChildGroup(string childId)
    {
        // Enforce data isolation: ensure user owns/belongs to child profile
        await Groups.AddToGroupAsync(Context.ConnectionId, $"child_{childId}");
    }
}

```

## 8. Audit Logging Schema & Event Tracking

All administrative and security actions are saved to the append-only `AuditLog` table.

```csharp
public class AuditService : IAuditService
{
    private readonly PocketMoneyDbContext _db;

    public async Task LogAsync(Guid? householdId, string actorId, ActorType actorType, AuditEventType eventType, object? details, string ipAddress)
    {
        var log = new AuditLog
        {
            HouseholdId = householdId,
            ActorId = actorId,
            ActorType = actorType,
            EventType = eventType,
            DetailsJson = details != null ? JsonSerializer.Serialize(details) : null,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}

```

## 9. Input Validation & Sanitization Rules (SRS §9)

Validation runs in three layers: **UI**, **API boundary** (DTO validation before any domain logic) and **database** (EF constraints as a final net). The rules below are normative for V1.

### 9.1 Trimming & Whitespace

* Bad characters and code injection attempt must be rejected at both UI and API tiers.
* All user-supplied strings are `Trim()`-ed at model binding, before any other rule.
* Values that are empty or whitespace-only after trimming are rejected (`400`) for required fields.

### 9.2 Max Lengths & Allowed Characters

| Field | Max length | Allowed characters / format | Input/Display |
| --- | --- | --- | --- |
| Child `AccountId` | 5 (fixed) | `^[0-9A-HJKLMNPRTVWXYZ]{5}$` — uppercase only; lowercase input is normalized to uppercase before lookup | Display |
| Child / Parent `DisplayName` | 100 | Unicode letters, digits, space, `-`, `'`, `.` | Input/Display |
| Household `DisplayName` | 60 | Unicode letters, digits, space, `-`, `'`, `.` | Input/Display |
| `CurrencySymbol` | 3 | Any 1–3 printable characters | Display |
| Transaction `Reason` | 255 | Free-form Unicode; control characters (U+0000–U+001F, U+007F) stripped; emoji restricted to `Constants.Transaction.ReasonEmojiWhitelist` — non-whitelisted emoji stripped at the API boundary | Input/Display |
| PINs (child & parent) | 4 | Exactly 4 digits, `^\d{4}$` | Input |
| Invitation email | 320 | RFC-5322 shape; verified again by Firebase at acceptance | Input/Display |

### 9.3 Sanitization Posture

* No HTML, markup, or SQL interpretation is applied to stored values — they persist exactly as received (post-trim).
* All database access goes through EF Core parameterized queries; no string-concatenated SQL, so no SQL-injection escaping is needed.
* The Blazor client renders all user text as text (auto-escaped); no raw HTML injection point exists.

### 9.4 Decimal Precision Rules

* Transaction `amount` must be **strictly greater than 0** and ≤ 9,999,999.999 (fits `Decimal(10,3)`).
* The fractional scale of `amount` must not exceed the household's `decimal_digits`. Values violating this are **rejected with `400` — never silently rounded**.
* `remaining_after` is computed server-side from `current_balance ± amount`; the client's preview is display-only.

### 9.5 Trailing-Zero Display (Frontend)

* Balances and amounts are rendered with **exactly** the household's `decimal_digits` decimal places (e.g. `$5.00` when `decimal_digits = 2`, `$5` when `0`).
* The API returns raw decimal values plus the household settings; formatting is a client responsibility per SRS §9.

## 10. Multi-Tenant Enforcement Model (SRS §3.1)

`Household` is the sole tenant boundary. Enforcement is layered — no single layer is trusted alone:

1. **Token resolution:** the auth middleware resolves the caller's identity into `HouseholdId` (parents) or `ChildId + HouseholdId` (children) claims. Requests without a resolved household are rejected before reaching controllers.
2. **Query scoping:** every tenant-scoped entity (`parents`, `children`, `transactions`, `household_invitations`, `audit_logs`) is filtered by `household_id` via EF Core global query filters seeded from the resolved claims. A query that omits the filter must be an explicit, reviewed exception.
3. **Child session restriction:** child tokens additionally scope reads to their own `child_id`, and only read-only timeline/balance endpoints are exposed to child roles (FR-S2).
4. **Real-time:** `LedgerHub.JoinChildGroup` verifies household ownership of the requested `child_id` before adding the connection to the group.
5. **Global by design:** `login_attempts` and `ip_bans` are intentionally **not** tenant-scoped (SRS §4.4 note).

**Household deletion:** physical deletion of the tenant subtree (household, parents, children, transactions, invitations). `audit_logs` and global `login_attempts` survive deletion for auditing, per FR-P1.

### Out of Scope for V1

V1 will NOT implement the followings:

* PostgreSQL Row-Level Security (RLS)

## 11. Database Migration Strategy

Schema evolves via **EF Core Code-First migrations** (`PocketMoney.Infrastructure/Migrations/`). The database is never hand-edited; the migration history is the source of truth for schema state.

### 11.1 Conventions

* **One logical change = one migration.** Descriptive imperative names: `AddChildrenSecurityStamp`, `AddIpBanBanCount`.
* Applied migrations are **immutable**: a shipped migration is never edited or deleted. Wrong step → ship a corrective migration on top.
* **No `EnsureCreated()`** — only `Migrate()`.
* Every schema change — including index additions (e.g. the `(child_id, created_at)` timeline index) — goes through a migration.
* Migrations are applied in CI/CD at deploy time (or via an explicit release step), with a database backup taken immediately before.

### 11.2 Append-Only Table Protection

`transactions`, `login_attempts`, and `audit_logs` are append-only by design. Migrations touching them may:

* ✔ add columns (nullable or with a default backfill value)
* ✔ add indexes

…and may **never**:

* ✘ drop them, drop their columns, or truncate data
* ✘ rewrite historical values (e.g. recomputing `remaining_after`)

Any migration that must rename or retype a column on an append-only table is a review-required exception, done additively: add new column → backfill → switch reads → drop old column in a *later* migration.

### 11.3 Tenant Data & Deletion

* Household deletion (FR-P1) is **application logic**, not migration logic. Migrations never delete tenant data.
* FK cascade behavior for the household subtree is defined in EF entity configurations; migrations must not introduce cascades that could touch `audit_logs` or global tables.

### 11.4 Seed Data

* No production seed data. Fresh databases are structurally empty; households are created through the parent onboarding flow.
* Test environments may use a separate dev-only seeding tool — never part of the migration chain.

### 11.5 Verification

* Each new migration must run cleanly against both (a) a fresh database and (b) a copy of the current production schema — enforced in CI.
* Destructive-looking operations (`DropTable`, `DropColumn`) outside the append-only carve-outs in §11.2 require explicit reviewer sign-off in the PR.

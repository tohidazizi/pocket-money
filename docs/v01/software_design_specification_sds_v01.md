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
│   ├── PocketMoney.Shared/           # Shared DTOs, Enums, Base31 Logic, Constants
│   │   ├── Consts/
│   │   ├── Dtos/
│   │   ├── Enums/
│   │   └── Utilities/
│   │
│   ├── PocketMoney.Domain/           # Core Entities & Value Objects (Code-First)
│   │   ├── Entities/
│   │   └── Interfaces/
│   │
│   ├── PocketMoney.Infrastructure/   # DbContext, EF Configurations, Repositories, Services
│   │   ├── Data/
│   │   ├── EntityConfigurations/
│   │   ├── Migrations/
│   │   └── Services/
│   │
│   ├── PocketMoney.Api/              # Controllers, SignalR Hubs, Middlewares, Auth Handlers
│   │   ├── Controllers/
│   │   ├── Hubs/
│   │   └── Middlewares/
│   │
│   └── PocketMoney.Client/           # Blazor WASM UI Components, State, Services
│       ├── Pages/
│       ├── Shared/
│       └── Services/

```

## 2. Domain Models & EF Core Code-First Schema

### 2.1 Shared Constants (`PocketMoney.Shared/Consts`)

```csharp
namespace PocketMoney.Shared.Consts;

public static class Constants
{
    public const Base31Alphabet = "0123456789ABCDEFGHJKLMNPRTVWXYZ"; // Base-31: no O I S U Q
    public const InactivityLimit = 5 * 60 * 1000; // FR-P: 5-minute parent lock
    
    public static class Child
    {
        public const byte AccountIdLength = 5;
        public const byte ChildrenMax = 9;
        public const int DisplayNameMaxLength = 9;
    }

    public static class Transaction
    {
        public const int ReasonMaxLength = 255;
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

### 2.3 EF Core Configuration Mappings (`PocketMoney.Infrastructure/EntityConfigurations`)

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
    private static readonly AccountIdLength = Constants.Child.AccountIdLength;
    private static readonly Alphabet = Constants.Base31Alphabet;

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
    // 1. Log attempt
    _dbContext.LoginAttempts.Add(new LoginAttempt
    {
        AccountId = accountId,
        IpAddress = clientInfo.ipAddress,
        ClientInfo = clientInfo, 
        IsSuccessful = false
    });

    // 2. Check Global IP Ban threshold (10 failures across any account in 24h)
    var ipFailures = await _dbContext.LoginAttempts
        .CountAsync(l => l.IpAddress == ipAddress && !l.IsSuccessful && l.CreatedAt >= DateTime.UtcNow.AddHours(-24)); // TODO: -24: add to Constants

    if (ipFailures >= 10) // TODO: 10: add to Constants
    {
        var existingBan = await _dbContext.IpBans.FirstOrDefaultAsync(b => b.IpAddress == ipAddress);
        int banCount = (existingBan?.BanCount ?? 0) + 1;
        
        DateTime bannedUntil = banCount switch
        {
            1 => DateTime.UtcNow.AddDays(1), // TODO: 1: add to Constants
            2 => DateTime.UtcNow.AddDays(7), // TODO: 7: add to Constants
            _ => DateTime.UtcNow.AddDays(30) // TODO: 30: add to Constants
        };

        if (existingBan != null)
        {
            existingBan.BanCount = banCount;
            existingBan.BannedUntil = bannedUntil;
        }
        else
        {
            _dbContext.IpBans.Add(new IpBan { IpAddress = ipAddress, BanCount = banCount, BannedUntil = bannedUntil });
        }
    }

    // 3. Child Specific Lockout Steps (3, 6, 9 attempts)
    if (child != null)
    {
        child.UnsuccessfulLoginAttempts++;
        if (child.UnsuccessfulLoginAttempts >= 9) // TODO: 9: add to Constants
        {
            child.IsPermanentlyLocked = true;
        }
        else if (child.UnsuccessfulLoginAttempts == 6) // TODO: 6: add to Constants
        {
            child.LockedUntil = DateTime.UtcNow.AddMinutes(15); // TODO: 15: add to Constants
        }
        else if (child.UnsuccessfulLoginAttempts == 3)
        {
            child.LockedUntil = DateTime.UtcNow.AddMinutes(5); // TODO: 5: add to Constants
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
    private const int InactivityTimeoutMs = 5 * 60 * 1000; // 5 Minutes
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
| `PUT` | `/api/v1/households/{id}/settings` | Parent JWT | sets household settings: currency & decimal accuracy |
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

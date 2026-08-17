namespace PocketMoney.Global;

/// <summary>Shared enums (SDS §2.2).</summary>
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
    ChildAccountLocked,
    ChildAccountUnlocked,
    ChildCurrencyChanged,
    ParentPinChanged,
    HouseholdSettingsUpdated,
    HouseholdDeleted,
    ParentInvited,
    ParentJoined,
    ParentInvitationCancelled
}

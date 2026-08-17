namespace PocketMoney.Global;

/// <summary>
/// Shared application-wide constants (SDS §2.1).
/// </summary>
public static class Constants
{
    /// <summary>Account IDs (FR-P3): Base-31 alphabet, O I S U Q excluded.</summary>
    public const string Base31Alphabet = "0123456789ABCDEFGHJKLMNPRTVWXYZ";

    /// <summary>Shared device guard (FR-P6): parent inactivity lock, milliseconds (5 minutes).</summary>
    public const int ParentInactivityLockMs = 5 * 60 * 1000;

    /// <summary>Household limits (FR-P2).</summary>
    public const byte MaxParentsPerHousehold = 2;

    public static class Child
    {
        public const byte AccountIdLength = 5;
        public const byte ChildrenMax = 9;
        public const int DisplayNameMaxLength = 100;

        /// <summary>Persistent child session lifetime in days (FR-C2).</summary>
        public const ushort TokenLifetimeDays = 365;
    }

    public static class Transaction
    {
        public const int ReasonMaxLength = 255;

        /// <summary>
        /// Family-friendly emoji whitelist for Transaction.Reason (SRS §9).
        /// Emoji characters outside this list are stripped at the API boundary.
        /// An entry implicitly includes its U+FE0F variation-selector form.
        /// </summary>
        public const string ReasonEmojiWhitelist = "😀😄😁😆🙂😉😊😍🥰😘😜😎🤩🥳😅😂🤣☺️👍👏🙌👋🤝💪🙏❤️🧡💛💚💙💜🤍🎉🎊🎁🎈⭐✨🏆🥇🏅💰💵💶💷💸🪙🌈☀️🌸🌻🌳🌙🐶🐱🐰🐼🦄🐢🦋🐝🍎🍌🍪🧁🎂🍕🍦🍿⚽🚲🎨🎮📚✏️🧩⏰";
    }

    /// <summary>Timeline pagination (FR-C4): keyset paging; see SDS §12.</summary>
    public static class Timeline
    {
        public const byte DefaultPageSize = 25;
        public const byte MaxPageSize = 100; // server-enforced ceiling
    }

    /// <summary>
    /// Child account lockout ladder (NFR-4). Tiers of <see cref="MaxFailedAttemptsPerLockout"/>
    /// cumulative failures; the counter resets to 0 on a successful login.
    /// </summary>
    public static class Lockout
    {
        public const byte MaxFailedAttemptsPerLockout = 3;
        public const byte FirstLockoutMinutes = 5;    // at 3 cumulative failures
        public const byte SecondLockoutMinutes = 15;  // at 6 cumulative failures
        public const byte PermanentLockThreshold = MaxFailedAttemptsPerLockout * 3; // 9 → permanent
    }

    /// <summary>Global IP ban (NFR-4). IP bans apply app-wide; static assets/CDN are exempt.</summary>
    public static class IpBan
    {
        public const byte FailureThreshold = 10;   // failures from one IP within the window
        public const byte FailureWindowHours = 24;
        public const byte FirstBanDays = 1;        // 24 hours
        public const byte SecondBanDays = 7;       // 1 week
        public const byte ThirdBanDays = 30;       // 1 month
    }
}

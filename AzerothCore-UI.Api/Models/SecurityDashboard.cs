namespace AzerothCore_UI.Api.Models;

public sealed record SecurityDashboard(
    int SuccessfulLogins24Hours,
    int FailedLogins24Hours,
    int DeniedActions24Hours,
    int EnabledUsers,
    int LockedUsers,
    IReadOnlyList<SecurityUserStatus> Users,
    IReadOnlyList<SuspiciousProbeSummary> SuspiciousProbes,
    CertificateStatus Certificate);

public sealed record SecurityUserStatus(
    ulong Id,
    string Username,
    string Role,
    bool Enabled,
    bool MustChangePassword,
    uint FailedLoginCount,
    DateTime? LockoutUntilUtc,
    DateTime? LastLoginAtUtc);

public sealed record SuspiciousProbeSummary(
    string RemoteAddress,
    int RequestCount,
    DateTime LastSeenUtc,
    string LastPath,
    int LastStatusCode);

public sealed record CertificateStatus(
    string Hostname,
    bool Found,
    bool CurrentlyValid,
    DateTime? ExpiresAtUtc,
    int? DaysRemaining,
    string Message);

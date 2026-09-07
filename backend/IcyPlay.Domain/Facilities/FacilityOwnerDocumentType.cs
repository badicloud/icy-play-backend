namespace IcyPlay.Domain.Facilities;

public static class FacilityOwnerDocumentType
{
    public const string BusinessPermit = "BusinessPermit";
    public const string GovernmentId = "GovernmentId";
    public const string DtiSecRegistration = "DtiSecRegistration";

    public static readonly IReadOnlyCollection<string> All =
    [
        BusinessPermit,
        GovernmentId,
        DtiSecRegistration
    ];

    public static bool IsSupported(string value) => All.Contains(value);
}

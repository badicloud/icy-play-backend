using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>Joins a facility to one amenity from the lookup.</summary>
public sealed class FacilityAmenity : Entity
{
    private FacilityAmenity()
    {
    }

    public FacilityAmenity(Guid facilityId, Guid amenityId, DateTimeOffset createdAt)
    {
        FacilityId = facilityId;
        AmenityId = amenityId;
        CreatedAt = createdAt;
    }

    public Guid FacilityId
    {
        get; private set;
    }
    public Facility Facility { get; private set; } = null!;
    public Guid AmenityId
    {
        get; private set;
    }
    public Amenity Amenity { get; private set; } = null!;
}

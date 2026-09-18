using FluentAssertions.Execution;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Facilities;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Audit;
using IcyPlay.Infrastructure.Facilities;
using IcyPlay.Infrastructure.Persistence;
using IcyPlay.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class SeedTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BuildVenueAsync_ShouldRaiseAVenueTheCatalogueWillActuallyOffer()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = new SeedService(
            context,
            CreateCourtService(context, catalog),
            catalog,
            new AuditLogger(context, new FixedTimeProvider(Now)),
            new PasswordHasher<User>(),
            new FixedTimeProvider(Now));

        // Act
        var seeded = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        // Assert: the point of seeding through the ordinary court service is
        // that what comes out is sellable. A venue built by writing rows can
        // look right in the database and be invisible to a customer.
        var offered = await catalog.ListCourtsAsync(null, CancellationToken.None);
        var mine = offered.Where(row => row.FacilityId == seeded.FacilityId).ToArray();

        using (new AssertionScope())
        {
            seeded.Courts.Should().HaveCount(5);

            // Five courts, each marked out four ways: basketball whole,
            // badminton two across, pickleball three, volleyball whole. Seven
            // playable courts per floor, thirty-five in all.
            mine.Should().HaveCount(35);
            mine.Select(row => row.SportName).Distinct().Should().BeEquivalentTo(
                "Basketball", "Badminton", "Pickleball", "Volleyball");

            // No events. They are priced differently and are not what this is
            // for.
            mine.Should().OnlyContain(row => row.Kind == "Sport");
        }
    }

    [Fact]
    public async Task BuildVenueAsync_ShouldPriceEachSportTheWayTheRealVenueDoes()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = new SeedService(
            context,
            CreateCourtService(context, catalog),
            catalog,
            new AuditLogger(context, new FixedTimeProvider(Now)),
            new PasswordHasher<User>(),
            new FixedTimeProvider(Now));

        // Act
        var seeded = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        // Assert
        var offered = await catalog.ListCourtsAsync(null, CancellationToken.None);
        var mine = offered.Where(row => row.FacilityId == seeded.FacilityId).ToArray();

        using (new AssertionScope())
        {
            mine.First(row => row.SportName == "Basketball").StandardHourlyRate.Should().Be(500m);
            mine.First(row => row.SportName == "Badminton").StandardHourlyRate.Should().Be(400m);
            mine.First(row => row.SportName == "Pickleball").StandardHourlyRate.Should().Be(300m);
            mine.First(row => row.SportName == "Pickleball").PeakHourlyRate.Should().Be(350m);
            mine.Should().OnlyContain(row => row.WeekendRate == 550m);
            // Five to eight, as the venue this copies keeps it.
            mine.Should().OnlyContain(row => row.PeakStartsAt == new TimeOnly(17, 0));
        }
    }

    [Fact]
    public async Task BuildVenueAsync_ShouldBuildADistinctVenueEachTime()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = new SeedService(
            context,
            CreateCourtService(context, catalog),
            catalog,
            new AuditLogger(context, new FixedTimeProvider(Now)),
            new PasswordHasher<User>(),
            new FixedTimeProvider(Now));

        // Act
        var first = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);
        var second = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        // Assert: pressing the button twice makes two venues rather than
        // failing on a name that is already taken.
        using (new AssertionScope())
        {
            second.FacilityId.Should().NotBe(first.FacilityId);
            second.SignInEmail.Should().NotBe(first.SignInEmail);
        }
    }

    [Fact]
    public async Task BuildVenueAsync_ShouldLeaveAnAccountThatCanActuallyBeOpened()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = new SeedService(
            context,
            CreateCourtService(context, catalog),
            catalog,
            new AuditLogger(context, new FixedTimeProvider(Now)),
            new PasswordHasher<User>(),
            new FixedTimeProvider(Now));

        // Act
        var seeded = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        // Assert: the only way into a seeded owner is a password reset, and a
        // reset sent to an invented address is an account nobody can open. So
        // an address with a real inbox behind it, and an account that is
        // verified so nothing else stands in the way of the reset.
        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == seeded.SignInEmail);

        using (new AssertionScope())
        {
            seeded.SignInEmail.Should().StartWith("demo-owner");
            seeded.SignInEmail.Should().EndWith("@mailinator.com");
            user.IsEmailVerified.Should().BeTrue();
            user.IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public async Task BuildVenueAsync_WhenTheDemoInboxIsTaken_ShouldSeedBesideItRatherThanRefuse()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = CreateSut(context, catalog);

        var first = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        // Act: seeding a second venue without removing the first is an
        // ordinary thing to do.
        var second = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        // Assert: refusing over the address would stop a run that has nothing
        // else wrong with it.
        using (new AssertionScope())
        {
            first.SignInEmail.Should().Be("demo-owner@mailinator.com");
            second.SignInEmail.Should().StartWith("demo-owner-");
            second.SignInEmail.Should().EndWith("@mailinator.com");
        }
    }

    [Fact]
    public async Task BuildVenueAsync_ShouldLeaveAPasswordThatCanBeReset()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var hasher = new PasswordHasher<User>();
        var sut = new SeedService(
            context,
            CreateCourtService(context, catalog),
            catalog,
            new AuditLogger(context, new FixedTimeProvider(Now)),
            hasher,
            new FixedTimeProvider(Now));

        var seeded = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        var user = await context.Users
            .SingleAsync(candidate => candidate.Email == seeded.SignInEmail);

        // Act: the first thing a reset does is read the stored hash, to check
        // the new password is not the old one.
        var reading = () => hasher.VerifyHashedPassword(user, user.PasswordHash, "AnyNewPassword1!");

        // Assert: a made-up string in that column is not "a password nobody
        // knows" — it is a column the hasher cannot read, and the reset throws
        // FormatException on it rather than failing politely.
        using (new AssertionScope())
        {
            reading.Should().NotThrow();
            reading().Should().Be(PasswordVerificationResult.Failed);
        }
    }

    [Fact]
    public async Task BuildVenueAsync_ShouldGiveTheOwnerTheRoleTheDeskAsksFor()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = new SeedService(
            context,
            CreateCourtService(context, catalog),
            catalog,
            new AuditLogger(context, new FixedTimeProvider(Now)),
            new PasswordHasher<User>(),
            new FixedTimeProvider(Now));

        // Act
        var seeded = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        // Assert: the desk asks for the role, not for a row in FacilityOwners.
        // Without it the account signs in perfectly well and can reach nothing
        // — which is worse than not being able to sign in, because it looks
        // like it worked.
        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == seeded.SignInEmail);

        var roles = await context.UserRoles
            .AsNoTracking()
            .Where(role => role.UserId == user.Id)
            .Select(role => role.Role)
            .ToArrayAsync();

        roles.Should().Contain(UserRoleName.FacilityOwner);
    }

    [Fact]
    public async Task BuildVenueAsync_ShouldUseTheAddressItIsGiven()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = new SeedService(
            context,
            CreateCourtService(context, catalog),
            catalog,
            new AuditLogger(context, new FixedTimeProvider(Now)),
            new PasswordHasher<User>(),
            new FixedTimeProvider(Now));

        var wanted = $"venue-{Guid.NewGuid():N}@example.com";

        // Act
        var seeded = await sut.BuildVenueAsync(Admin(), wanted, CancellationToken.None);

        // Assert: asked for rather than derived. A guessed address is one the
        // person seeding then edits in the database by hand, which is how a
        // seeded account ends up in a state nothing else can produce.
        seeded.SignInEmail.Should().Be(wanted);
    }

    [Fact]
    public async Task RemoveSeededAsync_ShouldTakeAwayTheVenueAndEverythingUnderIt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = CreateSut(context, catalog);

        var seeded = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        // Act
        var removed = await sut.RemoveSeededAsync(Admin(), CancellationToken.None);

        // Assert: a venue half removed is worse than one left standing — the
        // courts keep answering and the bookings behind them point at nothing.
        context.ChangeTracker.Clear();

        var offered = await catalog.ListCourtsAsync(null, CancellationToken.None);

        using (new AssertionScope())
        {
            removed.Venues.Should().Be(1);
            removed.Courts.Should().Be(5);

            (await context.FacilityOwners.CountAsync(owner => owner.Id == seeded.FacilityOwnerId))
                .Should().Be(0);
            (await context.Facilities.CountAsync(facility => facility.Id == seeded.FacilityId))
                .Should().Be(0);
            (await context.Courts.CountAsync(court => court.FacilityId == seeded.FacilityId))
                .Should().Be(0);

            // The account too. Left behind, it is an owner with nothing under
            // it, and the address can never be seeded again.
            (await context.Users.CountAsync(user => user.Email == seeded.SignInEmail))
                .Should().Be(0);

            // And gone from what a customer is shown, not merely from the
            // tables: the listing is cached, and a cached court that no longer
            // exists is an error the first person to tap it discovers.
            offered.Should().NotContain(row => row.FacilityId == seeded.FacilityId);
        }
    }

    [Fact]
    public async Task RemoveSeededAsync_ShouldLeaveAVenueItDidNotSeedAlone()
    {
        // Arrange: a real venue named exactly the way the seeder names its
        // own. Contrived, and that is the point — the name is what a rule like
        // "delete everything called Demo something" would go on.
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = CreateSut(context, catalog);

        var real = new User($"real-{Guid.NewGuid():N}@example.com", "Demo Venue ffffff", null);
        context.Users.Add(real);
        var realOwner = new FacilityOwner(real.Id, "Demo Sports Ventures ffffff", real.Email, null);
        context.FacilityOwners.Add(realOwner);
        await context.SaveChangesAsync();

        await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        // Act
        await sut.RemoveSeededAsync(Admin(), CancellationToken.None);

        // Assert: it carries no marker, so it is not demonstration data,
        // whatever it is called.
        context.ChangeTracker.Clear();

        using (new AssertionScope())
        {
            (await context.FacilityOwners.CountAsync(owner => owner.Id == realOwner.Id))
                .Should().Be(1);
            (await context.Users.CountAsync(user => user.Id == real.Id))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task RemoveSeededAsync_ShouldTakeTheBookingsMadeOnSeededCourts()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = CreateSut(context, catalog);

        var seeded = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        var bookable = await context.BookableCourts
            .AsNoTracking()
            .Where(candidate => context.Courts
                .Where(court => court.FacilityId == seeded.FacilityId)
                .Select(court => court.Id)
                .Contains(candidate.CourtId))
            .FirstAsync();

        var customer = new User($"customer-{Guid.NewGuid():N}@example.com", "A Customer", null);
        context.Users.Add(customer);

        var booking = new Booking(
            bookable.Id,
            customer.Id,
            BookingKind.Hourly,
            "Court 1",
            seeded.FacilityName,
            "Basketball",
            15m,
            new DateOnly(2026, 9, 20),
            new DateOnly(2026, 9, 20),
            30,
            Now);
        context.Bookings.Add(booking);
        context.BookingSlots.Add(new BookingSlot(
            booking.Id,
            bookable.CourtId,
            bookable.Id,
            new DateOnly(2026, 9, 20),
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            CourtRateKind.Standard,
            500m,
            15m,
            Now));
        await context.SaveChangesAsync();

        // Act
        var removed = await sut.RemoveSeededAsync(Admin(), CancellationToken.None);

        // Assert: a booking holds its bookable court under Restrict, because a
        // court with money against it is retired rather than removed. Removing
        // the venue without going through the bookings first does not leave
        // them orphaned — it fails outright, halfway through, having already
        // deleted whatever came before.
        context.ChangeTracker.Clear();

        using (new AssertionScope())
        {
            removed.Bookings.Should().Be(1);
            (await context.Bookings.CountAsync(candidate => candidate.Id == booking.Id)).Should().Be(0);
            (await context.BookingSlots.CountAsync(slot => slot.BookingId == booking.Id)).Should().Be(0);
            (await context.BookableCourts.CountAsync(candidate => candidate.Id == bookable.Id)).Should().Be(0);
        }
    }

    [Fact]
    public async Task SeededVenuesAsync_ShouldSayWhatEachVenueIsHolding()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = CreateSut(context, catalog);

        var seeded = await sut.BuildVenueAsync(Admin(), null, CancellationToken.None);

        // Act
        var venues = await sut.SeededVenuesAsync(CancellationToken.None);

        // Assert: the console shows these numbers before asking for a yes. A
        // count that is wrong here is a person agreeing to something other
        // than what happens.
        var mine = venues.Single(venue => venue.FacilityOwnerId == seeded.FacilityOwnerId);

        using (new AssertionScope())
        {
            mine.SignInEmail.Should().Be(seeded.SignInEmail);
            mine.Facilities.Should().Be(1);
            mine.Courts.Should().Be(5);
            mine.Bookings.Should().Be(0);
        }
    }

    [Fact]
    public async Task RemoveSeededAsync_WhenNothingWasSeeded_ShouldDoNothingRatherThanFail()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateSut(context, CreateCatalog(context));

        await sut.RemoveSeededAsync(Admin(), CancellationToken.None);

        // Act: pressing it twice is the ordinary way to find out whether the
        // first press worked.
        var again = await sut.RemoveSeededAsync(Admin(), CancellationToken.None);

        // Assert
        again.Should().Be(new SeedRemovalResult(0, 0, 0, 0));
    }

    private static SeedService CreateSut(AppDbContext context, IActivityCatalog catalog) => new(
        context,
        CreateCourtService(context, catalog),
        catalog,
        new AuditLogger(context, new FixedTimeProvider(Now)),
        new PasswordHasher<User>(),
        new FixedTimeProvider(Now));

    private static ActivityCatalog CreateCatalog(AppDbContext context) =>
        new(
            context,
            new MemoryCache(new MemoryCacheOptions()),
            new CatalogCacheSignal(),
            new FixedTimeProvider(Now),
            NullLogger<ActivityCatalog>.Instance);

    private static CourtService CreateCourtService(AppDbContext context, IActivityCatalog catalog) => new(
        context,
        catalog,
        new AuditLogger(context, new FixedTimeProvider(Now)),
        new CloudinaryAssetService(
            Options.Create(new CloudinaryOptions
            {
                CloudName = "icyplay-test",
                ApiKey = "123456789012345",
                ApiSecret = "test-api-secret"
            }),
            new FixedTimeProvider(Now)),
        new FixedTimeProvider(Now),
        NullLogger<CourtService>.Instance);

    private static AuditActor Admin() => new(Guid.NewGuid(), UserRoleName.PlatformAdmin);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

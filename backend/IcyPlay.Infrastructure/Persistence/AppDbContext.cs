using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Email;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Domain.OpenPlays;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SportsSeededAt = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<FacilityOwner> FacilityOwners => Set<FacilityOwner>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AccountInvitationToken> AccountInvitationTokens => Set<AccountInvitationToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<FacilityOwnerDocument> FacilityOwnerDocuments => Set<FacilityOwnerDocument>();
    public DbSet<FacilityOwnerContract> FacilityOwnerContracts => Set<FacilityOwnerContract>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<FacilityOperatingHour> FacilityOperatingHours => Set<FacilityOperatingHour>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<FacilityAmenity> FacilityAmenities => Set<FacilityAmenity>();
    public DbSet<Sport> Sports => Set<Sport>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<CourtSport> CourtSports => Set<CourtSport>();
    public DbSet<BookableCourt> BookableCourts => Set<BookableCourt>();
    public DbSet<FacilityAttendant> FacilityAttendants => Set<FacilityAttendant>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSlot> BookingSlots => Set<BookingSlot>();
    public DbSet<BookingUpgradeRequest> BookingUpgradeRequests => Set<BookingUpgradeRequest>();
    public DbSet<BookingMoveRecord> BookingMoves => Set<BookingMoveRecord>();
    public DbSet<BookingUpgradeSlot> BookingUpgradeSlots => Set<BookingUpgradeSlot>();
    public DbSet<CourtOperatingHour> CourtOperatingHours => Set<CourtOperatingHour>();
    public DbSet<MaintenancePeriod> MaintenancePeriods => Set<MaintenancePeriod>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<OpenPlay> OpenPlays => Set<OpenPlay>();
    public DbSet<OpenPlaySession> OpenPlaySessions => Set<OpenPlaySession>();
    public DbSet<OpenPlayRegistration> OpenPlayRegistrations => Set<OpenPlayRegistration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PhoneNumber).HasMaxLength(50);
            entity.Ignore(x => x.IsEmailVerified);
        });
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Role).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.Role }).IsUnique();
            entity.HasOne(x => x.User).WithMany(x => x.Roles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasOne(x => x.User).WithOne().HasForeignKey<Customer>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<FacilityOwner>(entity =>
        {
            entity.Property(x => x.BusinessRegistrationNumber).HasMaxLength(100);
            entity.ToTable("FacilityOwners");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.Property(x => x.BusinessName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.BillingEmail).HasMaxLength(256).IsRequired();
            entity.Property(x => x.GcashNumber).HasMaxLength(30);
            entity.Property(x => x.GcashAccountName).HasMaxLength(150);
            entity.Property(x => x.GcashQrCodeUrl).HasMaxLength(1000);
            entity.Property(x => x.PartialBookingExpiryMinutes)
                .HasDefaultValue(PaymentHold.DefaultMinutes);
            entity.Property(x => x.MoveNoticeDays)
                .HasDefaultValue(BookingMove.DefaultNoticeDays);
            entity.Property(x => x.BookingWindowDays)
                .HasDefaultValue(BookingWindow.DefaultDays);
            entity.Ignore(x => x.CanTakePayment);
            entity.Ignore(x => x.IsSeeded);
            // Filtered, because the question is only ever asked one way round:
            // which venues are demonstration data. Real venues are the vast
            // majority and none of them belong in this index.
            entity.HasIndex(x => x.SeededAt).HasFilter("[SeededAt] IS NOT NULL");
            entity.Property(x => x.BillingPhone).HasMaxLength(50);
            entity.HasOne(x => x.User).WithOne().HasForeignKey<FacilityOwner>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PlatformAdmin>(entity =>
        {
            entity.ToTable("PlatformAdmins");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasOne(x => x.User).WithOne().HasForeignKey<PlatformAdmin>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.IpAddress).HasMaxLength(45);
            entity.HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<EmailTemplate>(entity =>
        {
            entity.ToTable("EmailTemplates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(255).IsRequired();
            entity.HasIndex(x => new { x.Provider, x.Key }).IsUnique();

            entity.HasData(
                new
                {
                    Id = Guid.Parse("c9575d6e-7afd-5ed2-9368-fdd45dbf069b"),
                    Key = EmailTemplateKey.AccountVerification,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8278054L,
                    Subject = "Verify your IcyPlay email address",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("6b1f2ad4-93c7-4e58-8a0d-1c5e7f9b2d64"),
                    Key = EmailTemplateKey.PasswordReset,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8325458L,
                    Subject = "Reset your IcyPlay password",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("4a7d2f18-6c3b-4a91-b5e0-92d7c1f83b46"),
                    Key = EmailTemplateKey.FacilityOwnerInvitation,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8338524L,
                    Subject = "Activate your IcyPlay facility owner account",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("8c4d7f06-9e13-4a52-bd68-2f9a14e07c35"),
                    Key = EmailTemplateKey.FacilityAttendantInvitation,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8347695L,
                    // Names the venue rather than the platform. An invited
                    // attendant has never heard of IcyPlay, and a subject line
                    // carrying only our name reads as spam -- theirs is the one
                    // they recognise, and it explains why they are being
                    // written to at all.
                    Subject = "{{var:business_name}} has added you as a court attendant",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("b3f61c47-5d28-4e0a-9c73-8a1e5b2049df"),
                    Key = EmailTemplateKey.BookingPaymentReceived,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8347566L,
                    // Deliberately not "confirmed": this letter goes out the
                    // moment a receipt is uploaded, and a customer who reads the
                    // subject line and nothing else must not think they are
                    // booked.
                    Subject = "We have your payment \u2014 {{var:facility_name}} is checking it",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("6e94a20d-81cf-4b35-a7e8-3c5d90f61b28"),
                    Key = EmailTemplateKey.BookingPaymentSubmitted,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8347573L,
                    // Named in the subject because an attendant gets many of
                    // these: an inbox of identical lines cannot be worked
                    // through without opening every one.
                    Subject = "{{var:customer_name}} has paid for {{var:court_name}} \u2014 please confirm",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("d27b5e93-40a6-4c18-b9f2-71e8c3a56d04"),
                    Key = EmailTemplateKey.BookingConfirmed,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8347583L,
                    Subject = "Your court at {{var:facility_name}} is confirmed",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                // The three upgrade letters. Their HTML is in
                // docs/email-templates, and these ids are the copies of it
                // living in Mailjet.
                new
                {
                    Id = Guid.Parse("f5a81c30-6b47-4e92-8d15-c9f3a207e64b"),
                    Key = EmailTemplateKey.BookingUpgradeReceived,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8365287L,
                    // Deliberately not "confirmed", the same as the booking's
                    // own receipt letter: this goes out the moment the receipt
                    // is sent, and a customer who reads only the subject must
                    // not think they have the new court.
                    Subject = "We have your payment — {{var:facility_name}} is checking your upgrade",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("a19d4f62-7c85-4b03-9e2a-58d1b6f04c37"),
                    Key = EmailTemplateKey.BookingUpgradeSubmitted,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8365289L,
                    // Both courts in the subject. An attendant working an inbox
                    // needs to know which floor is being asked for before
                    // opening anything.
                    Subject = "{{var:customer_name}} has paid to move to {{var:to_court_name}}",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("c60e9b28-3af1-4d74-b85c-2e7a91d5c803"),
                    Key = EmailTemplateKey.BookingUpgradeApproved,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8365290L,
                    Subject = "Your booking has moved to {{var:to_court_name}}",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("e4a72d18-5c93-4f06-a1b7-6d20e893f5ca"),
                    Key = EmailTemplateKey.AccountNewSignIn,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8365643L,
                    // The device in the subject line, because that is the whole
                    // decision. "Chrome on Windows" and it was you, so the
                    // letter never needs opening; something you do not own, and
                    // you open it immediately.
                    Subject = "New sign-in to your IcyPlay account from {{var:device}}",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("7b3c05e1-9d46-4a28-83f7-1e5a2c904db6"),
                    Key = EmailTemplateKey.BookingMoved,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8365353L,
                    // "No action needed" in the subject line, because the desk
                    // has two queues that do want action and this letter does
                    // not. Without it, it gets opened as though it were work.
                    Subject = "{{var:customer_name}} moved to {{var:to_court_name}} — no action needed",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("b83f6a17-2e49-4d05-9c61-f4a0d7e25b98"),
                    Key = EmailTemplateKey.BookingDeclined,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8379592L,
                    // "Not accepted" in the subject, so somebody who reads
                    // nothing else does not turn up to play.
                    Subject = "Your booking at {{var:facility_name}} was not accepted",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("3d8e1f52-a4c7-4b19-9e06-7f2b5c81d4a3"),
                    Key = EmailTemplateKey.BookingMoveRequested,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8384428L,
                    // "Waiting for you" in the subject: this one IS work, unlike
                    // the old "no action needed" letter it replaces.
                    Subject = "{{var:customer_name}} wants to move to {{var:to_court_name}} — waiting for you",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("9a42c6e0-5b1d-4f83-a7e9-0c6d3b28f715"),
                    Key = EmailTemplateKey.BookingMoveApproved,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8384442L,
                    Subject = "Your booking has moved to {{var:court_name}}",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("e71b09d4-2c58-4a6f-b3d2-84f5a19c6e20"),
                    Key = EmailTemplateKey.BookingMoveDeclined,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8384444L,
                    // Where they still are, in the subject, so somebody who reads
                    // nothing else goes to the right court.
                    Subject = "Your booking has not moved — you're still on {{var:court_name}}",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                // The four an open play registration sends.
                new
                {
                    Id = Guid.Parse("3c8e1f52-7a94-4d06-b1e3-5f20c9a8d471"),
                    Key = EmailTemplateKey.OpenPlayPaymentReceived,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8392327L,
                    Subject = "We have your payment — {{var:facility_name}} is checking it",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("a5d27c09-3e61-4b8f-92a4-e07b16f3c582"),
                    Key = EmailTemplateKey.OpenPlayPaymentSubmitted,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8392333L,
                    Subject = "{{var:player_name}} has paid to join {{var:open_play_title}} — please confirm",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("f19b6e4a-08d3-4c72-a5e1-7b3d92c0f846"),
                    Key = EmailTemplateKey.OpenPlayConfirmed,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8392336L,
                    Subject = "You're registered for {{var:open_play_title}} on {{var:session_date}}",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("6b40d8e3-91f7-4a25-bc68-2e5a7f14d903"),
                    Key = EmailTemplateKey.OpenPlayDeclined,
                    Provider = EmailProviderName.Mailjet,
                    ExternalTemplateId = 8392337L,
                    // Not accepted, in the subject: somebody who reads nothing
                    // else must not turn up to play.
                    Subject = "Your registration for {{var:open_play_title}} was not accepted",
                    IsActive = true,
                    CreatedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt = (DateTimeOffset?)null
                });
        });
        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.ToTable("EmailVerificationTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
            entity.HasOne(x => x.User)
                .WithMany(x => x.EmailVerificationTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("PasswordResetTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
            entity.HasOne(x => x.User)
                .WithMany(x => x.PasswordResetTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Sport>(entity =>
        {
            entity.ToTable("Sports");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Kind)
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue(ActivityKind.Sport);
            entity.Property(x => x.ImagePublicId).HasMaxLength(300);
            entity.Property(x => x.ImageSecureUrl).HasMaxLength(1000);
            entity.Ignore(x => x.IsEvent);
            entity.HasIndex(x => x.Key).IsUnique();

            entity.HasData(
                new
                {
                    Id = Guid.Parse("74ad5832-6d45-5076-8b4c-d0c79472dd55"),
                    Key = "basketball",
                    Name = "Basketball",
                    Category = SportCategory.Court,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("8e21e167-3c31-5e9e-b203-f94339286f02"),
                    Key = "volleyball",
                    Name = "Volleyball",
                    Category = SportCategory.Court,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 20,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("0d37f0cd-9517-5a92-a320-caff82b9bcc6"),
                    Key = "futsal",
                    Name = "Futsal",
                    Category = SportCategory.Court,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 30,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("c1f88227-155e-5da6-b12b-479ed504e853"),
                    Key = "sepak-takraw",
                    Name = "Sepak takraw",
                    Category = SportCategory.Court,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 40,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("8bf0905e-65ba-5600-a868-0f28087f1d0b"),
                    Key = "badminton",
                    Name = "Badminton",
                    Category = SportCategory.Racket,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("a0f6b01f-491c-5bb0-ad8e-a42f19437ea3"),
                    Key = "tennis",
                    Name = "Tennis",
                    Category = SportCategory.Racket,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 20,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("17b95f71-a2b5-50a7-9fde-b07562272bed"),
                    Key = "table-tennis",
                    Name = "Table tennis",
                    Category = SportCategory.Racket,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 30,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("63573cf8-2b30-57f8-8ead-1da5098ea88e"),
                    Key = "pickleball",
                    Name = "Pickleball",
                    Category = SportCategory.Racket,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 40,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("dad7ebe3-f916-50f4-9317-92d01c75a061"),
                    Key = "squash",
                    Name = "Squash",
                    Category = SportCategory.Racket,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 50,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("84ed5c9c-3978-56d8-bc1d-9cac5ae1109e"),
                    Key = "boxing",
                    Name = "Boxing",
                    Category = SportCategory.Combat,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("f22fc719-1486-5215-9cc9-ec04de119105"),
                    Key = "taekwondo",
                    Name = "Taekwondo",
                    Category = SportCategory.Combat,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 20,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("2722a749-7853-5d82-a472-5c1ce07862c4"),
                    Key = "karate",
                    Name = "Karate",
                    Category = SportCategory.Combat,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 30,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("10a110db-d802-51a0-ada1-8f81bc655dcd"),
                    Key = "muay-thai",
                    Name = "Muay Thai",
                    Category = SportCategory.Combat,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 40,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("babc048a-e8a3-5572-8f03-c5a6a7f57d3f"),
                    Key = "fitness",
                    Name = "Fitness",
                    Category = SportCategory.Other,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("aee78495-e295-5753-8b47-de220d9d6f2b"),
                    Key = "dance",
                    Name = "Dance",
                    Category = SportCategory.Other,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 20,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("441948f8-b6cf-5a23-8074-b45e692b2d73"),
                    Key = "yoga",
                    Name = "Yoga",
                    Category = SportCategory.Other,
                    Kind = ActivityKind.Sport,
                    DisplayOrder = 30,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("1c4f8a2e-5b6d-5c7e-8f90-a1b2c3d4e5f6"),
                    Key = "birthday-party",
                    Name = "Birthday party",
                    Category = SportCategory.Events,
                    Kind = ActivityKind.Event,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("2d5a9b3f-6c7e-5d8f-9a01-b2c3d4e5f607"),
                    Key = "corporate-event",
                    Name = "Corporate event",
                    Category = SportCategory.Events,
                    Kind = ActivityKind.Event,
                    DisplayOrder = 20,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("3e6bac40-7d8f-5e90-ab12-c3d4e5f60718"),
                    Key = "tournament",
                    Name = "Tournament",
                    Category = SportCategory.Events,
                    Kind = ActivityKind.Event,
                    DisplayOrder = 30,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("4f7cbd51-8e90-5fa1-bc23-d4e5f6071829"),
                    Key = "training-clinic",
                    Name = "Training clinic",
                    Category = SportCategory.Events,
                    Kind = ActivityKind.Event,
                    DisplayOrder = 40,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("5a8dce62-9fa1-50b2-cd34-e5f60718293a"),
                    Key = "concert-or-show",
                    Name = "Concert or show",
                    Category = SportCategory.Events,
                    Kind = ActivityKind.Event,
                    DisplayOrder = 50,
                    IsActive = true,
                    CreatedAt = SportsSeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                });

        });

        modelBuilder.Entity<Court>(entity =>
        {
            entity.ToTable("Courts");
            entity.Ignore(x => x.HasPeakWindow);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.VenueType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Surface).HasMaxLength(50);
            entity.Property(x => x.SizeLabel).HasMaxLength(100);
            entity.Property(x => x.Equipment).HasMaxLength(500);
            // The pair every listing sorts by, and the scoping predicate.
            entity.HasIndex(x => new { x.FacilityId, x.DisplayOrder });
            entity.HasIndex(x => x.FacilityOwnerId);
            entity.HasOne(x => x.Facility)
                .WithMany(x => x.Courts)
                .HasForeignKey(x => x.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CourtSport>(entity =>
        {
            entity.ToTable("CourtSports");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CourtId, x.SportId }).IsUnique();
            // Money, so a fixed scale rather than a float: two decimal places
            // is what a peso amount is, and rounding drift in a price is the
            // kind of bug nobody reports until the receipts disagree.
            entity.Property(x => x.StandardHourlyRate).HasColumnType("decimal(10,2)");
            entity.Property(x => x.PeakHourlyRate).HasColumnType("decimal(10,2)");
            entity.Property(x => x.WeekendRate).HasColumnType("decimal(10,2)");
            entity.Property(x => x.HolidayRate).HasColumnType("decimal(10,2)");
            entity.Ignore(x => x.IsPriced);
            entity.Ignore(x => x.IsDivided);
            entity.Property(x => x.Divisions).HasDefaultValue(1);
            entity.HasOne(x => x.Court)
                .WithMany(x => x.Sports)
                .HasForeignKey(x => x.CourtId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Sport)
                .WithMany()
                .HasForeignKey(x => x.SportId)
                // A sport in use must not vanish from under the courts that
                // list it. Retire it instead.
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BookableCourt>(entity =>
        {
            entity.ToTable("BookableCourts");
            entity.HasKey(x => x.Id);
            // One part per number per pair, and the database says so rather
            // than the roster remembering to.
            entity.HasIndex(x => new { x.CourtSportId, x.DivisionNumber }).IsUnique();
            // What every availability check asks: what else is on this floor.
            entity.HasIndex(x => x.CourtId);
            entity.Property(x => x.Kind)
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue(BookableCourtKind.Whole);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Ignore(x => x.IsDivided);
            entity.HasOne(x => x.CourtSport)
                .WithMany(x => x.BookableCourts)
                .HasForeignKey(x => x.CourtSportId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Court)
                .WithMany()
                .HasForeignKey(x => x.CourtId)
                // The court reaches these through its sports, and letting both
                // paths cascade is a multiple-cascade-path error. The pair above
                // is the owner; this side only reads.
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FacilityAttendant>(entity =>
        {
            entity.ToTable("FacilityAttendants");
            entity.HasKey(x => x.Id);
            // One row per person per venue, whether they are on the desk today
            // or were taken off it. A retired row is the record of who confirmed
            // what, and it keeps the address spoken for.
            entity.HasIndex(x => new { x.FacilityId, x.UserId }).IsUnique();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.HasOne(x => x.Facility)
                .WithMany(x => x.Attendants)
                .HasForeignKey(x => x.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                // A person who has confirmed a booking must not vanish from
                // under it. Retire them instead.
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("Bookings");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CustomerUserId);
            entity.Property(x => x.Kind)
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue(BookingKind.Hourly);
            // What was bought, as it was named and priced then. A rename or a
            // re-price must not reach backwards into an agreement.
            entity.Property(x => x.CourtName).HasMaxLength(250).IsRequired();
            entity.Property(x => x.FacilityName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SportName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.PlatformHourlyRate).HasColumnType("decimal(10,2)");
            entity.Property(x => x.PaidTotal).HasColumnType("decimal(10,2)");
            entity.Property(x => x.CancellationReason).HasMaxLength(500);
            entity.Property(x => x.RejectionReason).HasMaxLength(30);
            entity.Property(x => x.RejectionNote).HasMaxLength(RejectReason.NoteLimit);
            entity.Property(x => x.ReceiptUrl).HasMaxLength(1000);
            // What a venue's console asks for: the bookings touching a stretch
            // of days.
            entity.HasIndex(x => new { x.StartDate, x.EndDate });
            entity.Ignore(x => x.BookedHours);
            entity.Ignore(x => x.RentalTotal);
            entity.Ignore(x => x.PlatformFeeTotal);
            entity.Ignore(x => x.Total);
            entity.HasOne(x => x.BookableCourt)
                .WithMany()
                .HasForeignKey(x => x.BookableCourtId)
                // A bookable court with money against it is retired, never
                // removed. Restrict says so rather than trusting everyone to
                // remember.
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BookingSlot>(entity =>
        {
            entity.ToTable("BookingSlots");
            entity.HasKey(x => x.Id);
            // The one question availability asks: what is held on this floor,
            // on this date.
            entity.HasIndex(x => new { x.CourtId, x.Date });
            entity.Property(x => x.RateKind)
                .HasMaxLength(20)
                .HasConversion<string>()
                .IsRequired();
            entity.Property(x => x.Amount).HasColumnType("decimal(10,2)");
            entity.Property(x => x.PlatformFee).HasColumnType("decimal(10,2)");
            entity.HasOne(x => x.Booking)
                .WithMany(x => x.Slots)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.BookableCourt)
                .WithMany()
                .HasForeignKey(x => x.BookableCourtId)
                // The booking's own cascade already reaches these rows; a
                // second path is one more than SQL Server allows.
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<BookingUpgradeRequest>(entity =>
        {
            entity.ToTable("BookingUpgradeRequests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ToCourtName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ReceiptUrl).HasMaxLength(1000);
            entity.Property(x => x.DeclineReason).HasMaxLength(500);
            entity.Property(x => x.MoveReason).HasMaxLength(30);
            entity.Property(x => x.MoveReasonNote).HasMaxLength(MoveReason.NoteLimit);
            // Money, so a fixed scale rather than a float: two decimal places
            // is what a peso amount is.
            entity.Property(x => x.RentalNow).HasColumnType("decimal(10,2)");
            entity.Property(x => x.RentalNew).HasColumnType("decimal(10,2)");
            entity.Property(x => x.BalanceDue).HasColumnType("decimal(10,2)");
            entity.Ignore(x => x.IsOpen);
            // The two questions asked of this table: what is open on this
            // booking, and what is waiting on this court.
            entity.HasIndex(x => new { x.BookingId, x.Status });
            entity.HasIndex(x => new { x.ToBookableCourtId, x.Status });
            entity.HasOne(x => x.Booking)
                .WithMany()
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ToBookableCourt)
                .WithMany()
                .HasForeignKey(x => x.ToBookableCourtId)
                // A court somebody is part way through paying to move onto is
                // retired, never removed. The cascade from the booking already
                // reaches these rows, and a second path is one more than SQL
                // Server allows.
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<BookingMoveRecord>(entity =>
        {
            entity.ToTable("BookingMoves");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(30);
            entity.Property(x => x.ReasonNote).HasMaxLength(MoveReason.NoteLimit);
            entity.Property(x => x.FromCourtName).HasMaxLength(200);
            entity.Property(x => x.ToCourtName).HasMaxLength(200);
            // The one question asked of it: what moved in this stretch of days.
            entity.HasIndex(x => x.MovedAt);
            entity.HasIndex(x => x.BookingId);
            entity.HasOne(x => x.Booking)
                .WithMany()
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookingUpgradeSlot>(entity =>
        {
            entity.ToTable("BookingUpgradeSlots");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RateKind)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(x => x.Amount).HasColumnType("decimal(10,2)");
            entity.Property(x => x.PlatformFee).HasColumnType("decimal(10,2)");
            // Availability asks one question of these: what is held on this
            // date while somebody pays for it.
            entity.HasIndex(x => x.Date);
            entity.HasOne(x => x.Request)
                .WithMany(x => x.Slots)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CourtOperatingHour>(entity =>
        {
            entity.ToTable("CourtOperatingHours");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CourtId, x.DayOfWeek }).IsUnique();
            entity.Ignore(x => x.IsClosed);
            entity.HasOne(x => x.Court)
                .WithMany(x => x.OperatingHours)
                .HasForeignKey(x => x.CourtId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Photo>(entity =>
        {
            entity.ToTable("Photos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PublicId).HasMaxLength(300).IsRequired();
            entity.Property(x => x.SecureUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Caption).HasMaxLength(300);
            entity.Ignore(x => x.BelongsToWholeFacility);
            // The gallery is always read for one subject, in display order.
            entity.HasIndex(x => new { x.FacilityId, x.CourtId, x.DisplayOrder });
            // The booking list asks a narrower question of the same table: what
            // this court looks like set up for this one sport.
            entity.HasIndex(x => new { x.CourtId, x.SportId });
            entity.HasOne(x => x.Facility)
                .WithMany()
                .HasForeignKey(x => x.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Court)
                .WithMany()
                .HasForeignKey(x => x.CourtId)
                // The facility cascade already reaches these rows; a second
                // cascade path is one more than SQL Server allows.
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Sport)
                .WithMany()
                .HasForeignKey(x => x.SportId)
                // Sports are a catalog the platform keeps, not something a
                // venue deletes. Restricting says so out loud rather than
                // quietly unpicking galleries if one ever were removed.
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MaintenancePeriod>(entity =>
        {
            entity.ToTable("MaintenancePeriods");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Ignore(x => x.AppliesToWholeFacility);
            // Answers "what is closed here, and when" in one seek, for both a
            // whole facility and a single court.
            entity.HasIndex(x => new { x.FacilityId, x.StartsAt, x.EndsAt });
            entity.HasIndex(x => x.CourtId);
            entity.HasOne(x => x.Facility)
                .WithMany()
                .HasForeignKey(x => x.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Court)
                .WithMany()
                .HasForeignKey(x => x.CourtId)
                // The facility cascade already reaches these rows; a second
                // cascade path is one more than SQL Server allows.
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<OpenPlay>(entity =>
        {
            entity.ToTable("OpenPlays");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(OpenPlayLimits.TitleLimit).IsRequired();
            entity.Property(x => x.Level).HasMaxLength(30).IsRequired();
            entity.Property(x => x.RegistrationFee).HasColumnType("decimal(10,2)");
            entity.Property(x => x.CoverPhotoPublicId).HasMaxLength(300);
            entity.Property(x => x.CoverPhotoUrl).HasMaxLength(1000);
            // Stored as its names ("Monday, Saturday") so a row read straight
            // out of the database says which days it runs.
            entity.Property(x => x.Days)
                .HasMaxLength(100)
                .HasConversion<string>()
                .IsRequired();
            entity.Ignore(x => x.DurationMinutes);
            entity.Ignore(x => x.IsSeeded);
            entity.Ignore(x => x.IsPublished);
            entity.Ignore(x => x.HasEnded);
            entity.Ignore(x => x.Status);
            entity.OwnsOne(x => x.EarlyBird, earlyBird =>
            {
                earlyBird.Property(x => x.DiscountKind)
                    .HasColumnName("EarlyBirdDiscountKind")
                    .HasMaxLength(20)
                    .IsRequired();
                earlyBird.Property(x => x.DiscountValue)
                    .HasColumnName("EarlyBirdDiscountValue")
                    .HasColumnType("decimal(10,2)");
                earlyBird.Property(x => x.LeadMinutes).HasColumnName("EarlyBirdLeadMinutes");
            });
            // The question availability asks of this table: what open plays
            // run on this floor.
            entity.HasIndex(x => new { x.CourtId, x.StartDate, x.EndDate });
            entity.HasIndex(x => x.FacilityId);
            entity.HasOne(x => x.Facility)
                .WithMany()
                .HasForeignKey(x => x.FacilityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.BookableCourt)
                .WithMany()
                .HasForeignKey(x => x.BookableCourtId)
                // Retired, never removed, the same as for bookings.
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OpenPlaySession>(entity =>
        {
            entity.ToTable("OpenPlaySessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CancellationReason).HasMaxLength(500);
            entity.Ignore(x => x.IsCancelled);
            // One row per date. Two players registering for the first time at
            // once must end up on the same session.
            entity.HasIndex(x => new { x.OpenPlayId, x.Date }).IsUnique();
            entity.HasOne(x => x.OpenPlay)
                .WithMany(x => x.Sessions)
                .HasForeignKey(x => x.OpenPlayId)
                // A session with money against it is cancelled, never deleted.
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OpenPlayRegistration>(entity =>
        {
            entity.ToTable("OpenPlayRegistrations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.SessionId, x.Status });
            entity.Property(x => x.RegistrationFee).HasColumnType("decimal(10,2)");
            entity.Property(x => x.Discount).HasColumnType("decimal(10,2)");
            entity.Property(x => x.PlatformFee).HasColumnType("decimal(10,2)");
            entity.Property(x => x.ReceiptUrl).HasMaxLength(1000);
            entity.Property(x => x.CancellationReason).HasMaxLength(500);
            entity.Property(x => x.RejectionReason).HasMaxLength(30);
            entity.Property(x => x.RejectionNote).HasMaxLength(RejectReason.NoteLimit);
            entity.Ignore(x => x.Total);
            entity.Ignore(x => x.IsRegistered);
            // A player's own list, and the check that they are not already on
            // a session before a second registration is taken.
            entity.HasIndex(x => new { x.CustomerUserId, x.SessionId });
            entity.HasOne(x => x.Session)
                .WithMany(x => x.Registrations)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActorRole).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(150).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.IpAddress).HasMaxLength(100);
            entity.Property(x => x.UserAgent).HasMaxLength(500);
            // Answers "what happened to this record, newest first" in one seek,
            // which is the only question the console asks of this table.
            entity.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAt });
            entity.HasIndex(x => x.ActorUserId);
            // No foreign key to Users on purpose: the trail must outlive the
            // account that made the change.
        });

        modelBuilder.Entity<AccountInvitationToken>(entity =>
        {
            entity.ToTable("AccountInvitationTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
            entity.HasOne(x => x.User)
                .WithMany(x => x.AccountInvitationTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FacilityOwnerDocument>(entity =>
        {
            entity.ToTable("FacilityOwnerDocuments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DocumentType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PublicId).HasMaxLength(300).IsRequired();
            entity.Property(x => x.SecureUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.FacilityOwnerId, x.DocumentType });
            entity.HasOne(x => x.FacilityOwner)
                .WithMany(x => x.Documents)
                .HasForeignKey(x => x.FacilityOwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FacilityOwnerContract>(entity =>
        {
            entity.ToTable("FacilityOwnerContracts");
            // Money and a percentage, so a fixed scale rather than a float:
            // rounding drift in a payout is the kind of bug nobody reports
            // until the statements disagree.
            entity.Property(x => x.PlatformHourlyRate)
                .HasColumnType("decimal(10,2)")
                .HasDefaultValue(PlatformRates.DefaultHourlyRate);
            entity.Property(x => x.CommissionPercentage)
                .HasColumnType("decimal(5,2)")
                .HasDefaultValue(PlatformRates.DefaultCommissionPercentage);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.DocumentPublicId).HasMaxLength(300);
            entity.Property(x => x.DocumentSecureUrl).HasMaxLength(1000);
            entity.Property(x => x.DocumentFileName).HasMaxLength(255);
            entity.Property(x => x.DocumentContentType).HasMaxLength(100);
            entity.Ignore(x => x.HasSignedAgreement);
            // Answers "is this owner bookable today" in one index seek.
            entity.HasIndex(x => new { x.FacilityOwnerId, x.StartDate, x.EndDate });
            entity.HasOne(x => x.FacilityOwner)
                .WithMany(x => x.Contracts)
                .HasForeignKey(x => x.FacilityOwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Facility>(entity =>
        {
            entity.ToTable("Facilities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(220).IsRequired();
            // The public URL segment, so a collision would send customers to the
            // wrong venue.
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.AddressLine1).HasMaxLength(300).IsRequired();
            entity.Property(x => x.AddressLine2).HasMaxLength(300);
            entity.Property(x => x.City).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Province).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PostalCode).HasMaxLength(20);
            entity.Property(x => x.Country).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TimeZone).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ContactPhone).HasMaxLength(50);
            entity.Property(x => x.ContactEmail).HasMaxLength(256);
            // Six decimal places is roughly a tenth of a metre, which is far
            // finer than a door needs.
            entity.Property(x => x.Latitude).HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasPrecision(9, 6);
            entity.HasIndex(x => x.FacilityOwnerId);
            entity.Ignore(x => x.HasCoordinates);
            entity.HasOne(x => x.FacilityOwner)
                .WithMany(x => x.Facilities)
                .HasForeignKey(x => x.FacilityOwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FacilityOperatingHour>(entity =>
        {
            entity.ToTable("FacilityOperatingHours");
            entity.HasKey(x => x.Id);
            // One row per day, so a facility cannot hold two answers for Monday.
            entity.HasIndex(x => new { x.FacilityId, x.DayOfWeek }).IsUnique();
            entity.Ignore(x => x.IsClosed);
            entity.HasOne(x => x.Facility)
                .WithMany(x => x.OperatingHours)
                .HasForeignKey(x => x.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Holiday>(entity =>
        {
            entity.ToTable("Holidays");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Kind).HasMaxLength(50).IsRequired();
            // Serves the date-ordered listing. A repeating holiday cannot be
            // matched on its stored date at all -- only its month and day count
            // -- so that comparison is made in memory, which a calendar's worth
            // of rows can afford.
            entity.HasIndex(x => x.Date);
            // The same holiday twice on one date is a duplicate, not a second
            // holiday; two different ones sharing a date is allowed, and does
            // happen when a proclamation lands on a regular holiday.
            entity.HasIndex(x => new { x.Name, x.Date }).IsUnique();

            entity.HasData(
                new
                {
                    Id = Guid.Parse("4f6bd1a7-9b9c-5d3e-8a1f-11a2b3c4d5e6"),
                    Name = "New Year's Day",
                    Date = new DateOnly(2026, 1, 1),
                    Kind = "Regular",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("5a7ce2b8-acad-5e4f-9b2a-22b3c4d5e6f7"),
                    Name = "Araw ng Kagitingan",
                    Date = new DateOnly(2026, 4, 9),
                    Kind = "Regular",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("6b8df3c9-bdbe-5f50-ac3b-33c4d5e6f708"),
                    Name = "Labor Day",
                    Date = new DateOnly(2026, 5, 1),
                    Kind = "Regular",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("7c9e04da-cecf-5061-bd4c-44d5e6f70819"),
                    Name = "Independence Day",
                    Date = new DateOnly(2026, 6, 12),
                    Kind = "Regular",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("8daf15eb-dfd0-5172-ce5d-55e6f708192a"),
                    Name = "Bonifacio Day",
                    Date = new DateOnly(2026, 11, 30),
                    Kind = "Regular",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("9eb026fc-e0e1-5283-df6e-66f708192a3b"),
                    Name = "Christmas Day",
                    Date = new DateOnly(2026, 12, 25),
                    Kind = "Regular",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("afc1370d-f1f2-5394-e07f-7708192a3b4c"),
                    Name = "Rizal Day",
                    Date = new DateOnly(2026, 12, 30),
                    Kind = "Regular",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("b0d2481e-0203-54a5-f180-88192a3b4c5d"),
                    Name = "Ninoy Aquino Day",
                    Date = new DateOnly(2026, 8, 21),
                    Kind = "Special non-working",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("c1e3592f-1314-55b6-0291-992a3b4c5d6e"),
                    Name = "All Saints' Day",
                    Date = new DateOnly(2026, 11, 1),
                    Kind = "Special non-working",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("d2f46a30-2425-56c7-13a2-aa3b4c5d6e7f"),
                    Name = "Feast of the Immaculate Conception",
                    Date = new DateOnly(2026, 12, 8),
                    Kind = "Special non-working",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("e3057b41-3536-57d8-24b3-bb4c5d6e7f80"),
                    Name = "Last day of the year",
                    Date = new DateOnly(2026, 12, 31),
                    Kind = "Special non-working",
                    RepeatsAnnually = true,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                });
        });

        modelBuilder.Entity<Amenity>(entity =>
        {
            entity.ToTable("Amenities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();

            entity.HasData(
                new
                {
                    Id = Guid.Parse("b6cef6fb-0320-5670-9e7c-2bea98d4f6a4"),
                    Key = "first-aid-kit",
                    Name = "First aid kit",
                    Category = AmenityCategory.Safety,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("c167c9f2-15ac-50a5-a552-22b436a0bff9"),
                    Key = "cctv",
                    Name = "CCTV",
                    Category = AmenityCategory.Safety,
                    DisplayOrder = 20,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("16557b82-105e-5195-826b-a606259bf214"),
                    Key = "fire-extinguisher",
                    Name = "Fire extinguisher",
                    Category = AmenityCategory.Safety,
                    DisplayOrder = 30,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("f0ace240-4169-5397-8de9-ceb8972b384c"),
                    Key = "emergency-exit",
                    Name = "Emergency exit",
                    Category = AmenityCategory.Safety,
                    DisplayOrder = 40,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("a2d94510-2c91-575a-9256-4e17ec0c369c"),
                    Key = "on-site-staff",
                    Name = "On-site staff",
                    Category = AmenityCategory.Safety,
                    DisplayOrder = 50,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("27bf719d-50f2-5e76-b392-1897b945e630"),
                    Key = "air-conditioning",
                    Name = "Air conditioning",
                    Category = AmenityCategory.Comfort,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("2ee9374d-1467-5426-a912-adebd6240cd0"),
                    Key = "showers",
                    Name = "Showers",
                    Category = AmenityCategory.Comfort,
                    DisplayOrder = 20,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("a98dcb63-d4dc-5211-bf53-693946c22c84"),
                    Key = "lockers",
                    Name = "Lockers",
                    Category = AmenityCategory.Comfort,
                    DisplayOrder = 30,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("afe375ab-a228-5854-9aa7-03382439f85f"),
                    Key = "restrooms",
                    Name = "Restrooms",
                    Category = AmenityCategory.Comfort,
                    DisplayOrder = 40,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("8e87c77b-a8c1-59c3-baf3-081595c7de44"),
                    Key = "drinking-water",
                    Name = "Drinking water",
                    Category = AmenityCategory.Comfort,
                    DisplayOrder = 50,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("5f10bf74-8ad5-51bb-9694-a12274375c3d"),
                    Key = "seating",
                    Name = "Spectator seating",
                    Category = AmenityCategory.Comfort,
                    DisplayOrder = 60,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("b94e2ec5-9db1-5be3-9891-67ded8fa9fcf"),
                    Key = "parking",
                    Name = "Parking",
                    Category = AmenityCategory.Access,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("83b54e86-367e-5fea-a9c8-24fe0b87d58c"),
                    Key = "wheelchair-access",
                    Name = "Wheelchair access",
                    Category = AmenityCategory.Access,
                    DisplayOrder = 20,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("595960f1-e817-56c6-be96-317fbe21687b"),
                    Key = "near-public-transport",
                    Name = "Near public transport",
                    Category = AmenityCategory.Access,
                    DisplayOrder = 30,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("3c65a092-7ced-580e-a6bf-592029e433a4"),
                    Key = "racket-rental",
                    Name = "Racket rental",
                    Category = AmenityCategory.Equipment,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("ffef0613-d0ac-5150-aec5-7f257da46a7c"),
                    Key = "ball-rental",
                    Name = "Ball rental",
                    Category = AmenityCategory.Equipment,
                    DisplayOrder = 20,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.Parse("6015e3d0-73be-51ce-93e6-5dff6362fc79"),
                    Key = "shuttlecock-for-sale",
                    Name = "Shuttlecock for sale",
                    Category = AmenityCategory.Equipment,
                    DisplayOrder = 30,
                    IsActive = true,
                    CreatedAt = SeededAt,
                    UpdatedAt = (DateTimeOffset?)null
                });
        });

        modelBuilder.Entity<FacilityAmenity>(entity =>
        {
            entity.ToTable("FacilityAmenities");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.FacilityId, x.AmenityId }).IsUnique();
            entity.HasOne(x => x.Facility)
                .WithMany(x => x.Amenities)
                .HasForeignKey(x => x.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Amenity)
                .WithMany()
                .HasForeignKey(x => x.AmenityId)
                // An amenity in use must not vanish from under the facilities
                // that reference it.
                .OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(modelBuilder);
    }
}

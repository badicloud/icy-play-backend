using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Email;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

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

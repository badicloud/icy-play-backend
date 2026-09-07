using IcyPlay.Domain.Email;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
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
    public DbSet<FacilityOwnerDocument> FacilityOwnerDocuments => Set<FacilityOwnerDocument>();
    public DbSet<FacilityOwnerContract> FacilityOwnerContracts => Set<FacilityOwnerContract>();

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
            // Answers "is this owner bookable today" in one index seek.
            entity.HasIndex(x => new { x.FacilityOwnerId, x.StartDate, x.EndDate });
            entity.HasOne(x => x.FacilityOwner)
                .WithMany(x => x.Contracts)
                .HasForeignKey(x => x.FacilityOwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}

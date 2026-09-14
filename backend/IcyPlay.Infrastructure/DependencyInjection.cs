using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.Email;
using IcyPlay.Application.Facilities;
using IcyPlay.Application.Identity;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Audit;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Email;
using IcyPlay.Infrastructure.Facilities;
using IcyPlay.Infrastructure.Identity;
using IcyPlay.Infrastructure.Persistence;
using IcyPlay.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IcyPlay.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(connectionString));

            services.AddScoped<IDbConnectionFactory>(_ =>
                new SqlConnectionFactory(connectionString));
        }

        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddHttpClient<IRecaptchaVerifier, RecaptchaVerifier>();
        services.Configure<MailjetOptions>(
            configuration.GetSection(MailjetOptions.SectionName));
        services.Configure<EmailVerificationOptions>(
            configuration.GetSection(EmailVerificationOptions.SectionName));
        services.Configure<PasswordResetOptions>(
            configuration.GetSection(PasswordResetOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IEmailTemplateStore, EmailTemplateStore>();
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();
        services.AddScoped<IPasswordResetEmailService, PasswordResetEmailService>();
        services.Configure<AccountInvitationOptions>(
            configuration.GetSection(AccountInvitationOptions.SectionName));
        services.Configure<BookingNotificationOptions>(
            configuration.GetSection(BookingNotificationOptions.SectionName));
        services.AddScoped<IAccountInvitationService, AccountInvitationService>();
        services.Configure<CloudinaryOptions>(
            configuration.GetSection(CloudinaryOptions.SectionName));
        services.AddScoped<ICloudinaryAssetService, CloudinaryAssetService>();
        services.Configure<PlatformAdminSeedOptions>(
            configuration.GetSection(PlatformAdminSeedOptions.SectionName));
        services.AddScoped<IPlatformAdminSeeder, PlatformAdminSeeder>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IFacilityOwnerOnboardingService, FacilityOwnerOnboardingService>();
        services.AddScoped<IFacilityOwnerEditService, FacilityOwnerEditService>();
        services.AddScoped<ICourtService, CourtService>();
        services.AddScoped<ISportService, SportService>();
        services.AddScoped<IHolidayService, HolidayService>();
        services.AddMemoryCache();
        // One signal outlives the requests that clear it.
        services.AddSingleton<CatalogCacheSignal>();
        services.AddScoped<IActivityCatalog, ActivityCatalog>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IFacilityAttendantService, FacilityAttendantService>();
        services.AddScoped<IBookingNotifier, BookingNotifier>();
        services.AddHttpClient<ITransactionalEmailSender, MailjetTransactionalEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.mailjet.com/v3.1/");
        });

        return services;
    }
}

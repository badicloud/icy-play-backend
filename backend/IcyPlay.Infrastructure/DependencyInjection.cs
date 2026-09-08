using IcyPlay.Application.Email;
using IcyPlay.Application.Facilities;
using IcyPlay.Application.Identity;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Identity;
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
        services.Configure<CloudinaryOptions>(
            configuration.GetSection(CloudinaryOptions.SectionName));
        services.AddScoped<ICloudinaryAssetService, CloudinaryAssetService>();
        services.Configure<PlatformAdminSeedOptions>(
            configuration.GetSection(PlatformAdminSeedOptions.SectionName));
        services.AddScoped<IPlatformAdminSeeder, PlatformAdminSeeder>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IFacilityOwnerOnboardingService, FacilityOwnerOnboardingService>();
        services.AddHttpClient<ITransactionalEmailSender, MailjetTransactionalEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.mailjet.com/v3.1/");
        });

        return services;
    }
}

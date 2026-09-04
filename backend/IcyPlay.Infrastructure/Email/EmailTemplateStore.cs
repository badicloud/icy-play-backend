using IcyPlay.Application.Email;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Email;

public sealed class EmailTemplateStore(AppDbContext dbContext) : IEmailTemplateStore
{
    public Task<EmailTemplateDescriptor?> GetActiveAsync(
        string key,
        string provider,
        CancellationToken cancellationToken)
    {
        return dbContext.EmailTemplates
            .AsNoTracking()
            .Where(template =>
                template.Key == key &&
                template.Provider == provider &&
                template.IsActive)
            .Select(template => new EmailTemplateDescriptor(
                template.ExternalTemplateId,
                template.Subject))
            .SingleOrDefaultAsync(cancellationToken);
    }
}

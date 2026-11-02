using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Entities;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Infrastructure.Persistence.Repositories;

/// <summary>Stores Admin accounts and the login audit in app_user and login_audit for the security module.</summary>
internal sealed class UserAccountStore(IDbContextFactory<SchedulerDbContext> contextFactory) : IUserAccountStore
{
    public async Task<bool> AnyUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AppUsers.AnyAsync(cancellationToken);
    }

    public async Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var lowered = username.Trim().ToLowerInvariant();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Username.ToLower() == lowered, cancellationToken);
        return user is null ? null : ToAccount(user);
    }

    public async Task<UserAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? null : ToAccount(user);
    }

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var users = await db.AppUsers.AsNoTracking().OrderBy(u => u.Username).ToListAsync(cancellationToken);
        return users.Select(ToAccount).ToList();
    }

    public async Task<bool> UsernameExistsAsync(string username, Guid? exceptUserId, CancellationToken cancellationToken = default)
    {
        var lowered = username.Trim().ToLowerInvariant();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AppUsers.AnyAsync(u => u.Username.ToLower() == lowered && u.Id != exceptUserId, cancellationToken);
    }

    public async Task CreateAsync(UserAccount user, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new AppUser(user.Id, user.Username.Trim(), user.PasswordHash, user.PasswordSalt, user.Role, user.IsActive,
            user.CreatedAtUtc == default ? DateTime.UtcNow : user.CreatedAtUtc)
        {
            RecoveryCodeHash = user.RecoveryCodeHash,
            RecoveryCodeSalt = user.RecoveryCodeSalt
        };
        db.AppUsers.Add(entity);
        await Save(db, cancellationToken);
    }

    public async Task UpdateAsync(UserAccount user, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken)
                     ?? throw new InvalidOperationException("The user no longer exists.");
        if (user.Version != 0)
        {
            db.Entry(entity).Property(e => e.Version).OriginalValue = user.Version;
        }

        entity.Username = user.Username.Trim();
        entity.PasswordHash = user.PasswordHash;
        entity.PasswordSalt = user.PasswordSalt;
        entity.Role = user.Role;
        entity.FailedLoginCount = user.FailedLoginCount;
        entity.LockoutEndUtc = user.LockoutEndUtc;
        entity.LastLoginAtUtc = user.LastLoginAtUtc;
        entity.IsActive = user.IsActive;
        entity.RecoveryCodeHash = user.RecoveryCodeHash;
        entity.RecoveryCodeSalt = user.RecoveryCodeSalt;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await Save(db, cancellationToken);
    }

    public async Task DeleteAsync(UserAccount user, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken)
                     ?? throw new UserAccountConflictException("The user no longer exists.");
        db.Entry(entity).Property(e => e.Version).OriginalValue = user.Version;
        db.AppUsers.Remove(entity);
        await Save(db, cancellationToken);
    }

    public async Task AddLoginAuditAsync(LoginAuditRecord record, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.LoginAudits.Add(new LoginAudit(record.UserId, record.UsernameAttempted, record.Succeeded, record.Reason, record.AttemptedAtUtc));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task Save(SchedulerDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new UserAccountConflictException("The account was changed by someone else after it was opened.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new UserAccountNameConflictException("Another user already has this username.", ex);
        }
    }

    private static UserAccount ToAccount(AppUser user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        PasswordHash = user.PasswordHash,
        PasswordSalt = user.PasswordSalt,
        Role = user.Role,
        FailedLoginCount = user.FailedLoginCount,
        LockoutEndUtc = user.LockoutEndUtc,
        LastLoginAtUtc = user.LastLoginAtUtc,
        IsActive = user.IsActive,
        CreatedAtUtc = user.CreatedAtUtc,
        RecoveryCodeHash = user.RecoveryCodeHash,
        RecoveryCodeSalt = user.RecoveryCodeSalt,
        Version = user.Version
    };
}

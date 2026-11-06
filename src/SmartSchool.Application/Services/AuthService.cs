using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services;

public class AuthService(
    IAppDbContext db,
    ICurrentUser current,
    IPasswordService passwords,
    ITokenService tokens,
    IOptions<SessionSettings> sessionOptions)
{
    private readonly SessionSettings _settings = sessionOptions.Value;

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var email = request.Email.NormalizeKey();

        // Login runs before any tenant context exists, so query filters are bypassed explicitly here.
        var user = await db.Users.IgnoreQueryFilters()
            .Include(u => u.Role).ThenInclude(r => r!.Permissions)
            .Include(u => u.Tenant)
            .Include(u => u.Schools)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == email && !u.IsDeleted, ct)
            ?? throw new UnauthorizedException();

        if (user.LockoutEndUtc > now)
            throw new UnauthorizedException("Too many failed attempts. Try again later.");

        if (!passwords.Verify(user.PasswordHash, request.Password))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= _settings.MaxFailedLogins)
            {
                user.LockoutEndUtc = now.AddMinutes(_settings.LockoutMinutes);
                user.FailedLoginCount = 0;
            }
            db.AuditLogs.Add(SignInAudit(user, AuditAction.SignInFailed, ip, "Failed sign-in (wrong password)"));
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException();
        }

        if (!user.IsActive)
            throw new ForbiddenException("This account is disabled.");

        if (user.Role is null || user.Role.IsDeleted)
            throw new ForbiddenException("This account has no valid role.");

        if (user.Role.Type != RoleType.SuperAdmin)
        {
            if (user.Tenant is null || !user.Tenant.HasAccess(now))
                throw new ForbiddenException("Your organization's subscription is inactive, suspended or expired. Contact your administrator.");
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginUtc = now;

        var session = new UserSession
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            CreatedAtUtc = now,
            LastSeenUtc = now,
            ExpiresAtUtc = now.AddMinutes(_settings.IdleTimeoutMinutes),
            IpAddress = ip,
            UserAgent = userAgent?.Length > 400 ? userAgent[..400] : userAgent
        };
        db.UserSessions.Add(session);
        db.AuditLogs.Add(SignInAudit(user, AuditAction.SignedIn, ip, "Signed in"));
        await db.SaveChangesAsync(ct);

        var (token, expires) = tokens.CreateToken(user, user.Role, user.Schools.Select(s => s.SchoolId), session.Id);

        return new LoginResponse
        {
            Token = token,
            ExpiresAtUtc = expires,
            User = await BuildCurrentUserAsync(user.Id, user.Role.Type, ct)
        };
    }

    /// <summary>Returns the profile, permissions and context options for the signed-in user.</summary>
    public async Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken ct)
    {
        if (current.UserId is not { } id || current.RoleType is not { } type)
            throw new UnauthorizedException("Not signed in.");
        return await BuildCurrentUserAsync(id, type, ct);
    }

    private async Task<CurrentUserDto> BuildCurrentUserAsync(Guid userId, RoleType type, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Include(u => u.Role).ThenInclude(r => r!.Permissions)
            .Include(u => u.Tenant)
            .Include(u => u.Schools).ThenInclude(s => s.School)
            .FirstAsync(u => u.Id == userId, ct);

        var dto = new CurrentUserDto
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            RoleName = user.Role!.Name,
            RoleType = user.Role.Type,
            TenantId = user.TenantId,
            TenantName = user.Tenant?.Name,
            Language = user.PreferredLanguage,
            Permissions = user.Role.Permissions.Select(p => p.Permission).OrderBy(p => p).ToList()
        };

        if (type == RoleType.SuperAdmin)
        {
            dto.Tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name)
                .Select(t => new LookupItem(t.Id, t.Name)).ToListAsync(ct);
            var tenantId = current.TenantId;
            if (tenantId is not null)
                dto.Schools = await db.Schools.AsNoTracking().Where(s => s.TenantId == tenantId)
                    .OrderBy(s => s.Name).Select(s => new LookupItem(s.Id, s.Name)).ToListAsync(ct);
        }
        else if (type == RoleType.TenantAdmin)
        {
            dto.Schools = await db.Schools.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.TenantId == user.TenantId && !s.IsDeleted)
                .OrderBy(s => s.Name).Select(s => new LookupItem(s.Id, s.Name)).ToListAsync(ct);
        }
        else
        {
            dto.Schools = user.Schools.Where(s => s.School is { IsDeleted: false })
                .OrderBy(s => s.School!.Name).Select(s => new LookupItem(s.SchoolId, s.School!.Name)).ToList();
        }

        return dto;
    }

    /// <summary>Keeps the session alive while the browser tab is open.</summary>
    public async Task<DateTime> HeartbeatAsync(CancellationToken ct)
    {
        var session = await GetActiveSessionAsync(ct);
        var now = DateTime.UtcNow;
        session.LastSeenUtc = now;
        session.ExpiresAtUtc = now.AddMinutes(_settings.IdleTimeoutMinutes);
        await db.SaveChangesAsync(ct);
        return session.ExpiresAtUtc;
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        if (current.SessionId is not { } sid) return;
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sid, ct);
        if (session is null || session.RevokedAtUtc is not null) return;
        session.RevokedAtUtc = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = session.TenantId, UserId = current.UserId, UserName = current.UserName, Action = AuditAction.SignedOut,
            EntityType = nameof(UserSession), EntityId = session.Id.ToString(), Summary = "Signed out", IpAddress = current.IpAddress
        });
        await db.SaveChangesAsync(ct);
    }

    private static AuditLog SignInAudit(AppUser user, AuditAction action, string? ip, string summary) => new()
    {
        TenantId = user.TenantId,
        SchoolId = user.Schools.Select(s => (Guid?)s.SchoolId).FirstOrDefault(),
        UserId = user.Id,
        UserName = user.FullName,
        Action = action,
        EntityType = nameof(AppUser),
        EntityId = user.Id.ToString(),
        Summary = $"{summary}: {user.Email}",
        IpAddress = ip
    };

    // ---------- My profile ----------

    public async Task<CurrentUserDto> UpdateProfileAsync(ProfileUpdateDto dto, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == current.UserId && !u.IsDeleted, ct)
            ?? throw new UnauthorizedException("Not signed in.");
        user.FirstName = dto.FirstName.Trim();
        user.LastName = dto.LastName.Trim();
        user.Phone = dto.Phone?.Trim();
        user.PreferredLanguage = dto.Language;
        await db.SaveChangesAsync(ct);
        return await GetCurrentUserAsync(ct);
    }

    /// <summary>Changes the caller's password and signs out their other sessions.</summary>
    public async Task ChangePasswordAsync(ChangePasswordDto dto, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == current.UserId && !u.IsDeleted, ct)
            ?? throw new UnauthorizedException("Not signed in.");
        if (!passwords.Verify(user.PasswordHash, dto.CurrentPassword))
            throw new ValidationException(nameof(dto.CurrentPassword), "The current password is not correct.");
        if (dto.NewPassword == dto.CurrentPassword)
            throw new ValidationException(nameof(dto.NewPassword), "Choose a password different from the current one.");
        if (!dto.NewPassword.Any(char.IsDigit) || !dto.NewPassword.Any(char.IsLetter))
            throw new ValidationException(nameof(dto.NewPassword), "Use at least 8 characters with letters and numbers.");

        user.PasswordHash = passwords.Hash(dto.NewPassword);
        var now = DateTime.UtcNow;
        var others = await db.UserSessions.Where(s => s.UserId == user.Id && s.Id != current.SessionId && s.RevokedAtUtc == null).ToListAsync(ct);
        others.ForEach(s => s.RevokedAtUtc = now);
        await db.SaveChangesAsync(ct);
    }

    private async Task<UserSession> GetActiveSessionAsync(CancellationToken ct)
    {
        var sid = current.SessionId ?? throw new UnauthorizedException("Session missing.");
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sid, ct);
        if (session is null || !session.IsActive(DateTime.UtcNow))
            throw new UnauthorizedException("Your session has expired. Please sign in again.");
        return session;
    }

    /// <summary>Recent sessions for the monitoring page, newest first, within the caller's tenant scope.</summary>
    public async Task<PagedResult<SessionDto>> GetSessionsAsync(PagedQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var q = db.UserSessions.AsNoTracking().Include(s => s.User).ThenInclude(u => u!.Tenant).AsQueryable();
        if (!current.IsSuperAdmin || current.TenantId is not null)
            q = q.Where(s => s.TenantId == current.TenantId);
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(s => s.User!.Email.Contains(query.Search) || s.User.FirstName.Contains(query.Search) || s.User.LastName.Contains(query.Search));

        return await q.OrderByDescending(s => s.LastSeenUtc)
            .Select(s => new SessionDto
            {
                Id = s.Id,
                UserEmail = s.User!.Email,
                UserName = s.User.FirstName + " " + s.User.LastName,
                TenantName = s.User.Tenant != null ? s.User.Tenant.Name : null,
                CreatedAtUtc = s.CreatedAtUtc,
                LastSeenUtc = s.LastSeenUtc,
                ExpiresAtUtc = s.ExpiresAtUtc,
                IsActive = s.RevokedAtUtc == null && s.ExpiresAtUtc > now,
                IpAddress = s.IpAddress,
                UserAgent = s.UserAgent
            })
            .ToPagedResultAsync(query, ct);
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException("Session", sessionId);
        if (!current.IsSuperAdmin && session.TenantId != current.TenantId)
            throw new ForbiddenException();
        session.RevokedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

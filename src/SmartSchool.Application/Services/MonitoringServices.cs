using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services;

/// <summary>Audit trail queries, scoped to what the caller administers.</summary>
public class AuditService(IAppDbContext db, ICurrentUser current)
{
    private IQueryable<AuditLog> Scoped()
    {
        var q = db.AuditLogs.AsNoTracking();
        if (current.IsSuperAdmin && current.TenantId is null) return q;
        q = q.Where(a => a.TenantId == current.TenantId);
        if (current.RoleType?.IsSchoolScoped() == true)
        {
            var schools = current.AllowedSchoolIds.ToList();
            q = q.Where(a => a.SchoolId != null && schools.Contains(a.SchoolId.Value));
        }
        else if (current.SchoolId is { } sid) q = q.Where(a => a.SchoolId == sid);
        return q;
    }

    public async Task<PagedResult<AuditLogDto>> ListAsync(PagedQuery query, string? entityType, AuditAction? action, string? entityId,
        DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var q = Scoped();
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(entityId)) q = q.Where(a => a.EntityId == entityId);
        if (action is not null) q = q.Where(a => a.Action == action);
        if (from is { } f) q = q.Where(a => a.TimestampUtc >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) q = q.Where(a => a.TimestampUtc < t.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(a => (a.UserName != null && a.UserName.Contains(s)) || (a.Summary != null && a.Summary.Contains(s)));
        }
        return await q.OrderByDescending(a => a.Id).Select(a => new AuditLogDto
        {
            Id = a.Id, TimestampUtc = a.TimestampUtc, UserName = a.UserName, Action = a.Action, EntityType = a.EntityType,
            EntityId = a.EntityId, Summary = a.Summary, Changes = a.Changes, IpAddress = a.IpAddress
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<List<string>> EntityTypesAsync(CancellationToken ct) =>
        await Scoped().Select(a => a.EntityType).Distinct().OrderBy(x => x).ToListAsync(ct);
}

public class TechnicalLogService(IAppDbContext db)
{
    public async Task<PagedResult<TechnicalLogDto>> ListAsync(PagedQuery query, string? level, CancellationToken ct)
    {
        var q = db.TechnicalLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(level)) q = q.Where(l => l.Level == level);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(l => l.Message.Contains(s) || (l.RequestPath != null && l.RequestPath.Contains(s)) || (l.TraceId != null && l.TraceId == s));
        }
        return await q.OrderByDescending(l => l.Id).Select(l => new TechnicalLogDto
        {
            Id = l.Id, TimestampUtc = l.TimestampUtc, Level = l.Level, Message = l.Message, Exception = l.Exception,
            SourceContext = l.SourceContext, RequestPath = l.RequestPath, TraceId = l.TraceId
        }).ToPagedResultAsync(query, ct);
    }

    /// <summary>Deletes technical logs older than the given number of days.</summary>
    public async Task<int> PurgeAsync(int olderThanDays, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, olderThanDays));
        return await db.TechnicalLogs.Where(l => l.TimestampUtc < cutoff).ExecuteDeleteAsync(ct);
    }
}

public partial class AnalyticsService(IAppDbContext db, ICurrentUser current)
{
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    public async Task RecordAsync(string path, CancellationToken ct)
    {
        if (current.UserId is not { } uid || current.RoleType is not { } role) return;
        var clean = path.Split('?', '#')[0].Trim('/').ToLowerInvariant();
        clean = GuidPattern().Replace(clean, "{id}");
        if (clean.Length == 0) clean = "dashboard";
        if (clean.Length > 200) clean = clean[..200];
        db.PageViews.Add(new PageView { UserId = uid, TenantId = current.TenantId, SchoolId = current.SchoolId, RoleType = role, Path = clean });
        await db.SaveChangesAsync(ct);
    }

    public async Task<UsageAnalyticsDto> GetAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var views = db.PageViews.AsNoTracking().Where(v => v.TimestampUtc >= since);
        var sessions = db.UserSessions.AsNoTracking().Where(s => s.CreatedAtUtc >= since);
        if (!(current.IsSuperAdmin && current.TenantId is null))
        {
            views = views.Where(v => v.TenantId == current.TenantId);
            sessions = sessions.Where(s => s.TenantId == current.TenantId);
        }

        var raw = await views.Select(v => new { v.TimestampUtc, v.UserId, v.Path, v.RoleType }).ToListAsync(ct);
        var daily = Enumerable.Range(0, days).Select(i => DateOnly.FromDateTime(since.AddDays(i)))
            .Select(d => new DailyUsageDto(d,
                raw.Count(v => DateOnly.FromDateTime(v.TimestampUtc) == d),
                raw.Where(v => DateOnly.FromDateTime(v.TimestampUtc) == d).Select(v => v.UserId).Distinct().Count()))
            .ToList();

        return new UsageAnalyticsDto
        {
            Days = days,
            TotalViews = raw.Count,
            ActiveUsers = raw.Select(v => v.UserId).Distinct().Count(),
            Sessions = await sessions.CountAsync(ct),
            Daily = daily,
            TopPages = raw.GroupBy(v => v.Path)
                .Select(g => new PageUsageDto(g.Key, g.Count(), g.Select(v => v.UserId).Distinct().Count()))
                .OrderByDescending(p => p.Views).Take(15).ToList(),
            ByRole = raw.GroupBy(v => v.RoleType)
                .Select(g => new RoleUsageDto(g.Key, g.Count(), g.Select(v => v.UserId).Distinct().Count()))
                .OrderBy(r => r.Role).ToList()
        };
    }

    public async Task<int> PurgeAsync(int olderThanDays, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(30, olderThanDays));
        return await db.PageViews.Where(v => v.TimestampUtc < cutoff).ExecuteDeleteAsync(ct);
    }
}

/// <summary>Tenant-defined status badges for students, teachers and parents.</summary>
public class StatusOptionService(IAppDbContext db, ICurrentUser current)
{
    public async Task<List<StatusOptionDto>> ListAsync(StatusCategory? category, CancellationToken ct)
    {
        var q = db.StatusOptions.AsNoTracking();
        if (category is not null) q = q.Where(s => s.Category == category);
        return await q.OrderBy(s => s.Category).ThenBy(s => s.SortOrder).ThenBy(s => s.Name).Select(s => new StatusOptionDto
        {
            Id = s.Id, Category = s.Category, Name = s.Name, Color = s.Color, SortOrder = s.SortOrder, IsActive = s.IsActive,
            UsageCount = s.Category == StatusCategory.Student ? db.Students.Count(x => x.StatusOptionId == s.Id)
                : s.Category == StatusCategory.Teacher ? db.Teachers.Count(x => x.StatusOptionId == s.Id)
                : db.Guardians.Count(x => x.StatusOptionId == s.Id)
        }).ToListAsync(ct);
    }

    public async Task<Guid> SaveAsync(Guid? id, StatusOptionDto dto, CancellationToken ct)
    {
        StatusOption option;
        if (id is { } existing)
            option = await db.StatusOptions.FirstOrDefaultAsync(s => s.Id == existing, ct) ?? throw new NotFoundException("Status option", existing);
        else
        {
            if (current.TenantId is null) throw new ValidationException("tenant", "Choose a tenant in the top bar first.");
            option = new StatusOption { Category = dto.Category };
            db.StatusOptions.Add(option);
        }
        var name = dto.Name.Trim();
        if (await db.StatusOptions.AnyAsync(s => s.Category == option.Category && s.Name == name && s.Id != option.Id, ct))
            throw new ValidationException(nameof(dto.Name), "This status already exists.");
        option.Name = name;
        option.Color = dto.Color.ToLowerInvariant();
        option.SortOrder = dto.SortOrder;
        option.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return option.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var option = await db.StatusOptions.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Status option", id);
        var used = await db.Students.AnyAsync(s => s.StatusOptionId == id, ct) || await db.Teachers.AnyAsync(t => t.StatusOptionId == id, ct)
            || await db.Guardians.AnyAsync(g => g.StatusOptionId == id, ct);
        if (used) throw new ConflictException("This status is in use. Deactivate it instead.");
        db.StatusOptions.Remove(option);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Validates a status chosen for a person of the given category.</summary>
    public async Task EnsureValidAsync(Guid? id, StatusCategory category, CancellationToken ct)
    {
        if (id is { } sid && !await db.StatusOptions.AnyAsync(s => s.Id == sid && s.Category == category && s.IsActive, ct))
            throw new ValidationException("statusOptionId", "Choose an active status of the right kind.");
    }
}

using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services.Academics;

public class AttendanceService(IAppDbContext db, ICurrentUser current, AccessScope scope, SchoolContext ctx, FileService files, NotificationService notifications)
{
    // ---------- Absence reasons ----------

    public async Task<List<AbsenceReasonDto>> ListReasonsAsync(CancellationToken ct) =>
        await db.AbsenceReasons.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new AbsenceReasonDto { Id = r.Id, Name = r.Name, IsExcused = r.IsExcused, RequiresDocument = r.RequiresDocument, IsActive = r.IsActive })
            .ToListAsync(ct);

    public async Task<Guid> SaveReasonAsync(Guid? id, AbsenceReasonDto dto, CancellationToken ct)
    {
        AbsenceReason reason;
        if (id is { } existing)
            reason = await db.AbsenceReasons.FirstOrDefaultAsync(r => r.Id == existing, ct) ?? throw new NotFoundException("Absence reason", existing);
        else
        {
            reason = new AbsenceReason { SchoolId = await ctx.RequireSchoolAsync(ct) };
            db.AbsenceReasons.Add(reason);
        }
        reason.Name = dto.Name.Trim();
        reason.IsExcused = dto.IsExcused;
        reason.RequiresDocument = dto.RequiresDocument;
        reason.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return reason.Id;
    }

    // ---------- Daily sheet ----------

    public async Task<AttendanceSheetDto> GetSheetAsync(Guid classId, DateOnly date, CancellationToken ct)
    {
        await scope.EnsureTeachesAsync(classId, null, ct);
        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class", classId);

        var students = await scope.ActiveStudentsOf(classId).AsNoTracking()
            .Select(s => new { s.Id, s.StudentNo, Name = s.FirstName + " " + s.LastName }).ToListAsync(ct);
        var ids = students.Select(s => s.Id).ToList();
        var records = await db.AttendanceRecords.AsNoTracking().Include(r => r.Document)
            .Where(r => r.Date == date && ids.Contains(r.StudentId)).ToListAsync(ct);

        return new AttendanceSheetDto
        {
            ClassId = classId,
            ClassName = cls.Name,
            Date = date,
            IsRecorded = records.Count > 0,
            IsValidated = records.Count > 0 && records.All(r => r.IsValidated),
            Rows = students.Select(s =>
            {
                var r = records.FirstOrDefault(x => x.StudentId == s.Id);
                return new AttendanceRowDto
                {
                    StudentId = s.Id, StudentNo = s.StudentNo, StudentName = s.Name, RecordId = r?.Id,
                    Status = r?.Status ?? AttendanceStatus.Present, ArrivalTime = r?.ArrivalTime, ReasonId = r?.ReasonId,
                    Note = r?.Note, Document = FileService.ToDto(r?.Document)
                };
            }).ToList()
        };
    }

    public async Task SaveSheetAsync(AttendanceSaveDto dto, CancellationToken ct)
    {
        var classId = dto.ClassId ?? throw new ValidationException(nameof(dto.ClassId), "Select a class.");
        await scope.EnsureTeachesAsync(classId, null, ct);
        var cls = await db.Classes.Include(c => c.AcademicYear).FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class", classId);
        if (dto.Date > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            throw new ValidationException(nameof(dto.Date), "Attendance cannot be recorded for a future date.");
        if (dto.Date < cls.AcademicYear!.StartDate || dto.Date > cls.AcademicYear.EndDate)
            throw new ValidationException(nameof(dto.Date), "The date is outside this class's academic year.");

        var enrolled = await scope.ActiveStudentsOf(classId).Select(s => s.Id).ToListAsync(ct);
        var ids = dto.Rows.Select(r => r.StudentId).Where(enrolled.Contains).ToList();
        var existing = await db.AttendanceRecords.Where(r => r.Date == dto.Date && ids.Contains(r.StudentId)).ToListAsync(ct);
        var canValidate = current.HasPermission(Shared.Security.Permissions.Attendance.Validate);
        if (existing.Any(r => r.IsValidated) && !canValidate)
            throw new ForbiddenException("This attendance sheet has been validated and can no longer be changed.");

        var reasons = await db.AbsenceReasons.Select(r => r.Id).ToListAsync(ct);
        var newlyAbsent = new List<Guid>();
        foreach (var row in dto.Rows.Where(r => ids.Contains(r.StudentId)))
        {
            if (row.ReasonId is { } rid && !reasons.Contains(rid))
                throw new ValidationException("reasonId", "Unknown absence reason.");
            var record = existing.FirstOrDefault(r => r.StudentId == row.StudentId);
            if (row.Status == AttendanceStatus.Absent && record?.Status != AttendanceStatus.Absent) newlyAbsent.Add(row.StudentId);
            if (record is null)
            {
                record = new AttendanceRecord { SchoolId = cls.SchoolId, ClassId = classId, StudentId = row.StudentId, Date = dto.Date };
                db.AttendanceRecords.Add(record);
            }
            record.ClassId = classId;
            record.Status = row.Status;
            record.ArrivalTime = row.Status == AttendanceStatus.Late ? row.ArrivalTime : null;
            record.ReasonId = row.Status == AttendanceStatus.Present ? null : row.ReasonId;
            record.Note = row.Note?.Trim();
        }

        // Tell parents the same day their child was marked absent.
        if (newlyAbsent.Count > 0)
        {
            var names = await db.Students.Where(s => newlyAbsent.Contains(s.Id)).Select(s => new { s.Id, Name = s.FirstName + " " + s.LastName }).ToListAsync(ct);
            foreach (var s in names)
                notifications.Queue(await notifications.FamilyUserIdsAsync([s.Id], false, true, ct), $"Absence: {s.Name}",
                    $"{s.Name} was marked absent on {dto.Date:dddd yyyy-MM-dd} ({cls.Name}). You can send a justification from the Attendance page.",
                    "attendance", "Justify absence", NotificationSeverity.Warning, cls.TenantId, cls.SchoolId, $"Parents of {s.Name}");
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ValidateAsync(Guid classId, DateOnly date, CancellationToken ct)
    {
        await scope.EnsureCanSeeClassAsync(classId, ct);
        var records = await db.AttendanceRecords.Where(r => r.ClassId == classId && r.Date == date).ToListAsync(ct);
        if (records.Count == 0) throw new ValidationException("date", "Record attendance for this day before validating it.");
        var now = DateTime.UtcNow;
        foreach (var r in records.Where(r => !r.IsValidated))
        {
            r.IsValidated = true;
            r.ValidatedBy = current.UserId;
            r.ValidatedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------- Records and reports ----------

    public async Task<PagedResult<AttendanceRecordDto>> ListAsync(PagedQuery query, Guid? classId, Guid? studentId, DateOnly? from, DateOnly? to,
        bool absencesOnly, CancellationToken ct)
    {
        var q = await ScopedRecordsAsync(ct);
        if (classId is not null) q = q.Where(r => r.ClassId == classId);
        if (studentId is not null) q = q.Where(r => r.StudentId == studentId);
        if (from is not null) q = q.Where(r => r.Date >= from);
        if (to is not null) q = q.Where(r => r.Date <= to);
        if (absencesOnly) q = q.Where(r => r.Status != AttendanceStatus.Present);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(r => r.Student!.FirstName.Contains(s) || r.Student.LastName.Contains(s));
        }

        return await q.OrderByDescending(r => r.Date).ThenBy(r => r.Student!.LastName)
            .Select(r => new AttendanceRecordDto
            {
                Id = r.Id,
                Date = r.Date,
                StudentId = r.StudentId,
                StudentName = r.Student!.FirstName + " " + r.Student.LastName,
                ClassName = r.Class!.Name,
                Status = r.Status,
                ArrivalTime = r.ArrivalTime,
                ReasonName = r.Reason != null ? r.Reason.Name : null,
                IsExcused = r.Status == AttendanceStatus.Excused || (r.Reason != null && r.Reason.IsExcused),
                Note = r.Note,
                Document = r.Document == null ? null : new FileDto { Id = r.Document.Id, FileName = r.Document.FileName, ContentType = r.Document.ContentType, Size = r.Document.Size },
                IsValidated = r.IsValidated
            }).ToPagedResultAsync(query, ct);
    }

    public async Task<List<AttendanceSummaryDto>> SummaryAsync(Guid? classId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (classId is { } cid) await scope.EnsureCanSeeClassAsync(cid, ct);
        var q = await ScopedRecordsAsync(ct);
        if (classId is not null) q = q.Where(r => r.ClassId == classId);
        if (from is not null) q = q.Where(r => r.Date >= from);
        if (to is not null) q = q.Where(r => r.Date <= to);

        var rows = await q.GroupBy(r => new { r.StudentId, r.Student!.StudentNo, r.Student.FirstName, r.Student.LastName })
            .Select(g => new AttendanceSummaryDto
            {
                StudentId = g.Key.StudentId,
                StudentNo = g.Key.StudentNo,
                StudentName = g.Key.FirstName + " " + g.Key.LastName,
                DaysRecorded = g.Count(),
                Present = g.Count(r => r.Status == AttendanceStatus.Present),
                Absent = g.Count(r => r.Status == AttendanceStatus.Absent),
                Late = g.Count(r => r.Status == AttendanceStatus.Late),
                Excused = g.Count(r => r.Status == AttendanceStatus.Excused || (r.Status == AttendanceStatus.Absent && r.Reason != null && r.Reason.IsExcused)),
                Unjustified = g.Count(r => r.Status == AttendanceStatus.Absent && (r.Reason == null || !r.Reason.IsExcused))
            })
            .OrderBy(r => r.StudentName).ToListAsync(ct);
        foreach (var r in rows)
            r.AttendanceRate = r.DaysRecorded == 0 ? 100 : Math.Round((r.Present + r.Late) * 100m / r.DaysRecorded, 1);
        return rows;
    }

    /// <summary>Staff or the student's family can attach a reason, a note and a supporting document to an absence.</summary>
    public async Task JustifyAsync(Guid recordId, AttendanceJustifyDto dto, (Stream Content, string Name, string? Type, long Length)? upload, CancellationToken ct)
    {
        var record = await db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct) ?? throw new NotFoundException("Attendance record", recordId);
        if (scope.IsFamily) await scope.EnsureCanSeeStudentAsync(record.StudentId, ct);
        else await scope.EnsureTeachesAsync(record.ClassId, null, ct);
        if (record.Status == AttendanceStatus.Present)
            throw new ValidationException("status", "Only absences and late arrivals can be justified.");
        if (record.IsValidated && scope.IsFamily)
            throw new ValidationException("status", "This day has already been validated by the school.");

        if (dto.ReasonId is { } rid)
        {
            if (!await db.AbsenceReasons.AnyAsync(r => r.Id == rid && r.IsActive, ct))
                throw new ValidationException(nameof(dto.ReasonId), "Select an active absence reason.");
            record.ReasonId = rid;
        }
        if (dto.Note is not null) record.Note = dto.Note.Trim();
        if (upload is { } u)
        {
            var file = await files.UploadAsync(record.SchoolId, u.Content, u.Name, u.Type, u.Length, ct);
            record.DocumentId = file.Id;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<IQueryable<AttendanceRecord>> ScopedRecordsAsync(CancellationToken ct)
    {
        var q = db.AttendanceRecords.AsNoTracking();
        if (scope.IsFamily)
        {
            var mine = await scope.MyStudentIdsAsync(ct);
            return q.Where(r => mine.Contains(r.StudentId));
        }
        var classes = await scope.VisibleClassIdsAsync(ct);
        return classes is null ? q : q.Where(r => classes.Contains(r.ClassId));
    }
}

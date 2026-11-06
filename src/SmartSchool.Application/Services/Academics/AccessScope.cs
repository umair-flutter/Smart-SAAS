using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services.Academics;

/// <summary>
/// Row-level visibility on top of tenant/school filters:
/// administrators see the whole school, teachers see the classes they teach,
/// parents see their children and students see themselves.
/// </summary>
public class AccessScope(IAppDbContext db, ICurrentUser current)
{
    private Guid? _teacherId;
    private bool _teacherLoaded;
    private List<Guid>? _studentIds;

    public RoleType Role => current.RoleType ?? RoleType.Student;

    /// <summary>SuperAdmin, TenantAdmin and SchoolAdmin.</summary>
    public bool IsAdmin => Role <= RoleType.SchoolAdmin;
    public bool IsTeacher => Role == RoleType.Teacher;
    public bool IsFamily => Role is RoleType.Parent or RoleType.Student;

    public async Task<Guid?> TeacherIdAsync(CancellationToken ct)
    {
        if (!_teacherLoaded)
        {
            _teacherId = current.UserId is { } uid
                ? await db.Teachers.Where(t => t.UserId == uid).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct)
                : null;
            _teacherLoaded = true;
        }
        return _teacherId;
    }

    /// <summary>Students the caller is linked to: their children (parent) or themselves (student).</summary>
    public async Task<List<Guid>> MyStudentIdsAsync(CancellationToken ct)
    {
        if (_studentIds is not null) return _studentIds;
        var uid = current.UserId;
        _studentIds = Role switch
        {
            RoleType.Student => await db.Students.Where(s => s.UserId == uid).Select(s => s.Id).ToListAsync(ct),
            RoleType.Parent => await db.StudentGuardians.Where(g => g.Guardian!.UserId == uid && !g.Student!.IsDeleted).Select(g => g.StudentId).ToListAsync(ct),
            _ => []
        };
        return _studentIds;
    }

    public async Task<List<MyStudentDto>> MyStudentsAsync(CancellationToken ct)
    {
        var ids = await MyStudentIdsAsync(ct);
        return await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id)).OrderBy(s => s.FirstName)
            .Select(s => new MyStudentDto
            {
                StudentId = s.Id,
                FullName = s.FirstName + " " + s.LastName,
                ClassId = s.Enrollments.Where(e => !e.IsDeleted && e.Status == EnrollmentStatus.Active && e.Class!.AcademicYear!.IsCurrent).Select(e => (Guid?)e.ClassId).FirstOrDefault(),
                ClassName = s.Enrollments.Where(e => !e.IsDeleted && e.Status == EnrollmentStatus.Active && e.Class!.AcademicYear!.IsCurrent).Select(e => e.Class!.Name).FirstOrDefault()
            }).ToListAsync(ct);
    }

    /// <summary>Classes the caller may see, or null for "every class of the school" (administrators).</summary>
    public async Task<List<Guid>?> VisibleClassIdsAsync(CancellationToken ct)
    {
        if (IsAdmin) return null;
        if (IsTeacher)
        {
            var tid = await TeacherIdAsync(ct);
            if (tid is null) return [];
            return await db.Classes.Where(c => c.HomeroomTeacherId == tid || c.Assignments.Any(a => a.TeacherId == tid && !a.IsDeleted))
                .Select(c => c.Id).ToListAsync(ct);
        }
        var students = await MyStudentIdsAsync(ct);
        return await db.Enrollments.Where(e => students.Contains(e.StudentId)).Select(e => e.ClassId).Distinct().ToListAsync(ct);
    }

    /// <summary>Requires the caller to teach in the class (and the subject, when given). Administrators always pass.</summary>
    public async Task EnsureTeachesAsync(Guid classId, Guid? subjectId, CancellationToken ct)
    {
        if (IsAdmin) return;
        if (!IsTeacher) throw new ForbiddenException();
        var tid = await TeacherIdAsync(ct) ?? throw new ForbiddenException("Your account is not linked to a teacher record.");
        var ok = subjectId is { } sid
            ? await db.TeachingAssignments.AnyAsync(a => a.ClassId == classId && a.SubjectId == sid && a.TeacherId == tid, ct)
            : await db.Classes.AnyAsync(c => c.Id == classId && (c.HomeroomTeacherId == tid || c.Assignments.Any(a => a.TeacherId == tid && !a.IsDeleted)), ct);
        if (!ok) throw new ForbiddenException(subjectId is null ? "You do not teach this class." : "You do not teach this subject in this class.");
    }

    public async Task<bool> IsHomeroomTeacherAsync(Guid classId, CancellationToken ct)
    {
        if (!IsTeacher) return false;
        var tid = await TeacherIdAsync(ct);
        return tid is not null && await db.Classes.AnyAsync(c => c.Id == classId && c.HomeroomTeacherId == tid, ct);
    }

    public async Task<bool> CanSeeStudentAsync(Guid studentId, CancellationToken ct)
    {
        if (IsAdmin) return await db.Students.AnyAsync(s => s.Id == studentId, ct);
        if (IsFamily) return (await MyStudentIdsAsync(ct)).Contains(studentId);
        var classes = await VisibleClassIdsAsync(ct) ?? [];
        return await db.Enrollments.AnyAsync(e => e.StudentId == studentId && classes.Contains(e.ClassId), ct);
    }

    public async Task EnsureCanSeeStudentAsync(Guid studentId, CancellationToken ct)
    {
        if (!await CanSeeStudentAsync(studentId, ct)) throw new NotFoundException("Student", studentId);
    }

    public async Task EnsureCanSeeClassAsync(Guid classId, CancellationToken ct)
    {
        var visible = await VisibleClassIdsAsync(ct);
        if (visible is not null && !visible.Contains(classId)) throw new NotFoundException("Class", classId);
        if (visible is null && !await db.Classes.AnyAsync(c => c.Id == classId, ct)) throw new NotFoundException("Class", classId);
    }

    /// <summary>Active students of a class, ordered by name.</summary>
    public IQueryable<Student> ActiveStudentsOf(Guid classId) =>
        db.Students.Where(s => s.Enrollments.Any(e => e.ClassId == classId && e.Status == EnrollmentStatus.Active && !e.IsDeleted))
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName);
}

/// <summary>Uploads and access-checked downloads of documents.</summary>
public class FileService(IAppDbContext db, IFileStorage storage, ICurrentUser current, AccessScope scope)
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".odt", ".zip"
    };

    public async Task<StoredFile> UploadAsync(Guid schoolId, Stream content, string fileName, string? contentType, long length, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName);
        if (!Allowed.Contains(ext))
            throw new ValidationException("file", "This file type is not allowed. Use PDF, images, Office documents, text or ZIP.");
        if (length <= 0 || length > MaxBytes)
            throw new ValidationException("file", "Files must be between 1 byte and 10 MB.");
        var tenantId = current.TenantId ?? await db.Schools.Where(s => s.Id == schoolId).Select(s => s.TenantId).FirstAsync(ct);

        var path = await storage.SaveAsync(tenantId, content, ext.ToLowerInvariant(), ct);
        var file = new StoredFile
        {
            TenantId = tenantId,
            SchoolId = schoolId,
            FileName = Path.GetFileName(fileName).Length > 200 ? Path.GetFileName(fileName)[..200] : Path.GetFileName(fileName),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Size = length,
            StoragePath = path
        };
        db.Files.Add(file);
        return file;
    }

    /// <summary>Opens a file after checking the caller may see the record it is attached to.</summary>
    public async Task<(Stream Content, string FileName, string ContentType)> OpenAsync(Guid fileId, CancellationToken ct)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct) ?? throw new NotFoundException("File", fileId);
        if (!await CanAccessAsync(fileId, ct)) throw new NotFoundException("File", fileId);
        return (await storage.OpenReadAsync(file.StoragePath, ct), file.FileName, file.ContentType);
    }

    private async Task<bool> CanAccessAsync(Guid fileId, CancellationToken ct)
    {
        if (scope.IsAdmin) return true;

        var record = await db.AttendanceRecords.Where(r => r.DocumentId == fileId).Select(r => new { r.StudentId }).FirstOrDefaultAsync(ct);
        if (record is not null) return await scope.CanSeeStudentAsync(record.StudentId, ct);

        var classes = await scope.VisibleClassIdsAsync(ct) ?? [];

        var homework = await db.Homework.Where(h => h.AttachmentId == fileId).Select(h => new { h.ClassId, h.IsPublished }).FirstOrDefaultAsync(ct);
        if (homework is not null) return classes.Contains(homework.ClassId) && (homework.IsPublished || scope.IsTeacher);

        var submission = await db.HomeworkSubmissions.Where(s => s.AttachmentId == fileId)
            .Select(s => new { s.StudentId, s.Homework!.ClassId }).FirstOrDefaultAsync(ct);
        if (submission is not null)
            return scope.IsTeacher ? classes.Contains(submission.ClassId) : (await scope.MyStudentIdsAsync(ct)).Contains(submission.StudentId);

        var exam = await db.Exams.Where(e => e.DocumentId == fileId).Select(e => new { e.ClassId }).FirstOrDefaultAsync(ct);
        if (exam is not null) return classes.Contains(exam.ClassId);

        return false;
    }

    public static FileDto? ToDto(StoredFile? f) =>
        f is null ? null : new FileDto { Id = f.Id, FileName = f.FileName, ContentType = f.ContentType, Size = f.Size };
}

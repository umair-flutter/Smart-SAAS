using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services.Academics;

public class HomeworkService(IAppDbContext db, ICurrentUser current, AccessScope scope, SchoolContext ctx, FileService files, NotificationService notifications)
{
    public async Task<PagedResult<HomeworkDto>> ListAsync(PagedQuery query, Guid? classId, Guid? subjectId, Guid? studentId, bool upcomingOnly, CancellationToken ct)
    {
        var classesInYear = await ctx.ClassesInActiveYearAsync(ct);
        var q = db.Homework.AsNoTracking().Where(h => classesInYear.Any(c => c.Id == h.ClassId));
        var visible = await scope.VisibleClassIdsAsync(ct);
        if (visible is not null) q = q.Where(h => visible.Contains(h.ClassId));

        Guid? me = null;
        if (scope.IsFamily)
        {
            q = q.Where(h => h.IsPublished);
            var mine = await scope.MyStudentIdsAsync(ct);
            me = studentId is { } s && mine.Contains(s) ? s : mine.FirstOrDefault();
            if (me is { } sid)
            {
                var childClasses = await db.Enrollments.Where(e => e.StudentId == sid && e.Status == EnrollmentStatus.Active).Select(e => e.ClassId).ToListAsync(ct);
                q = q.Where(h => childClasses.Contains(h.ClassId));
            }
        }
        if (classId is not null) q = q.Where(h => h.ClassId == classId);
        if (subjectId is not null) q = q.Where(h => h.SubjectId == subjectId);
        if (upcomingOnly) q = q.Where(h => h.DueAtUtc >= DateTime.UtcNow.Date);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(h => h.Title.Contains(term) || h.Subject!.Name.Contains(term));
        }

        q = query.SortBy?.ToLowerInvariant() switch
        {
            "assigned" => q.OrderByDirection(h => h.AssignedAtUtc, query.SortDesc),
            "title" => q.OrderByDirection(h => h.Title, query.SortDesc),
            _ => query.SortDesc ? q.OrderBy(h => h.DueAtUtc) : q.OrderByDescending(h => h.DueAtUtc)
        };

        return await q.Select(h => new HomeworkDto
        {
            Id = h.Id,
            ClassId = h.ClassId,
            ClassName = h.Class!.Name,
            SubjectId = h.SubjectId,
            SubjectName = h.Subject!.Name,
            SubjectColor = h.Subject.Color,
            TeacherName = h.Teacher != null ? h.Teacher.FirstName + " " + h.Teacher.LastName : null,
            Title = h.Title,
            AssignedAtUtc = h.AssignedAtUtc,
            DueAtUtc = h.DueAtUtc,
            MaxScore = h.MaxScore,
            AllowLateSubmission = h.AllowLateSubmission,
            IsPublished = h.IsPublished,
            StudentCount = h.Class.Enrollments.Count(e => !e.IsDeleted && e.Status == EnrollmentStatus.Active),
            SubmittedCount = h.Submissions.Count(s => !s.IsDeleted),
            LateCount = h.Submissions.Count(s => !s.IsDeleted && s.IsLate),
            GradedCount = h.Submissions.Count(s => !s.IsDeleted && s.Status != SubmissionStatus.Submitted),
            MyStatus = me == null ? null : h.Submissions.Where(s => s.StudentId == me && !s.IsDeleted).Select(s => (SubmissionStatus?)s.Status).FirstOrDefault(),
            MyIsLate = me == null ? null : h.Submissions.Where(s => s.StudentId == me && !s.IsDeleted).Select(s => (bool?)s.IsLate).FirstOrDefault(),
            MyScore = me == null ? null : h.Submissions.Where(s => s.StudentId == me && !s.IsDeleted && s.Status != SubmissionStatus.Submitted).Select(s => s.Score).FirstOrDefault()
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<HomeworkDetailDto> GetAsync(Guid id, Guid? studentId, CancellationToken ct)
    {
        var h = await db.Homework.AsNoTracking()
            .Include(x => x.Class).Include(x => x.Subject).Include(x => x.Teacher).Include(x => x.Attachment)
            .Include(x => x.Submissions).ThenInclude(s => s.Student)
            .Include(x => x.Submissions).ThenInclude(s => s.Attachment)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Homework", id);
        await scope.EnsureCanSeeClassAsync(h.ClassId, ct);
        if (scope.IsFamily && !h.IsPublished) throw new NotFoundException("Homework", id);

        var subs = h.Submissions.Where(s => !s.IsDeleted && s.Student is not null).ToList();
        if (scope.IsFamily)
        {
            var mine = await scope.MyStudentIdsAsync(ct);
            var who = studentId is { } s && mine.Contains(s) ? s : mine.FirstOrDefault();
            subs = subs.Where(x => x.StudentId == who).ToList();
        }

        var dto = new HomeworkDetailDto
        {
            Id = h.Id, ClassId = h.ClassId, ClassName = h.Class!.Name, SubjectId = h.SubjectId, SubjectName = h.Subject!.Name,
            SubjectColor = h.Subject.Color, TeacherName = h.Teacher?.FullName, Title = h.Title, Instructions = h.Instructions,
            AssignedAtUtc = h.AssignedAtUtc, DueAtUtc = h.DueAtUtc, MaxScore = h.MaxScore, AllowLateSubmission = h.AllowLateSubmission,
            IsPublished = h.IsPublished, Attachment = FileService.ToDto(h.Attachment),
            SubmittedCount = subs.Count, LateCount = subs.Count(s => s.IsLate), GradedCount = subs.Count(s => s.Status != SubmissionStatus.Submitted),
            Submissions = subs.OrderBy(s => s.Student!.LastName).Select(s => new SubmissionDto
            {
                Id = s.Id, StudentId = s.StudentId, StudentName = s.Student!.FullName, SubmittedAtUtc = s.SubmittedAtUtc, Text = s.Text,
                Attachment = FileService.ToDto(s.Attachment), IsLate = s.IsLate, Status = s.Status,
                // Families see the score only once the work has been graded.
                Score = scope.IsFamily && s.Status == SubmissionStatus.Submitted ? null : s.Score,
                Feedback = scope.IsFamily && s.Status == SubmissionStatus.Submitted ? null : s.Feedback,
                GradedAtUtc = s.GradedAtUtc
            }).ToList()
        };

        if (!scope.IsFamily)
        {
            var submitted = subs.Select(s => s.StudentId).ToHashSet();
            var students = await scope.ActiveStudentsOf(h.ClassId).AsNoTracking().Select(s => new { s.Id, Name = s.FirstName + " " + s.LastName }).ToListAsync(ct);
            dto.StudentCount = students.Count;
            dto.Missing = students.Where(s => !submitted.Contains(s.Id)).Select(s => new LookupItem(s.Id, s.Name)).ToList();
        }
        return dto;
    }

    public async Task<Guid> SaveAsync(Guid? id, HomeworkEditDto dto, CancellationToken ct)
    {
        var classId = dto.ClassId ?? throw new ValidationException(nameof(dto.ClassId), "Select a class.");
        var subjectId = dto.SubjectId ?? throw new ValidationException(nameof(dto.SubjectId), "Select a subject.");
        await scope.EnsureTeachesAsync(classId, subjectId, ct);
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class", classId);
        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId && s.SchoolId == cls.SchoolId, ct))
            throw new ValidationException(nameof(dto.SubjectId), "Select a subject of this school.");

        Homework h;
        if (id is { } existing)
        {
            h = await db.Homework.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Homework", existing);
            await scope.EnsureTeachesAsync(h.ClassId, h.SubjectId, ct);
        }
        else
        {
            h = new Homework { SchoolId = cls.SchoolId, AssignedAtUtc = DateTime.UtcNow };
            db.Homework.Add(h);
        }
        var announce = dto.IsPublished && (id is null || !h.IsPublished);
        if (dto.DueAtUtc <= h.AssignedAtUtc)
            throw new ValidationException(nameof(dto.DueAtUtc), "The due date must be after the date the homework is set.");

        h.ClassId = classId;
        h.SubjectId = subjectId;
        h.TeacherId = await scope.TeacherIdAsync(ct)
            ?? await db.TeachingAssignments.Where(a => a.ClassId == classId && a.SubjectId == subjectId).Select(a => (Guid?)a.TeacherId).FirstOrDefaultAsync(ct);
        h.Title = dto.Title.Trim();
        h.Instructions = dto.Instructions?.Trim();
        h.DueAtUtc = DateTime.SpecifyKind(dto.DueAtUtc, DateTimeKind.Utc);
        h.MaxScore = dto.MaxScore;
        h.AllowLateSubmission = dto.AllowLateSubmission;
        h.IsPublished = dto.IsPublished;
        await db.SaveChangesAsync(ct);
        if (announce)
        {
            var subject = await db.Subjects.Where(s => s.Id == subjectId).Select(s => s.Name).FirstAsync(ct);
            notifications.Queue(await notifications.ClassFamilyUserIdsAsync(classId, true, true, ct), $"New homework: {h.Title}",
                $"{subject} · {cls.Name} · due {h.DueAtUtc:yyyy-MM-dd HH:mm} UTC", $"homework/{h.Id}", "Open homework",
                Shared.Enums.NotificationSeverity.Info, cls.TenantId, cls.SchoolId, $"Students and parents of {cls.Name}");
            await db.SaveChangesAsync(ct);
        }
        return h.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var h = await db.Homework.Include(x => x.Submissions).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Homework", id);
        await scope.EnsureTeachesAsync(h.ClassId, h.SubjectId, ct);
        if (h.Submissions.Any(s => !s.IsDeleted))
            throw new ConflictException("Students have already submitted this homework. Unpublish it instead of deleting it.");
        db.Homework.Remove(h);
        await db.SaveChangesAsync(ct);
    }

    public async Task<FileDto> AttachAsync(Guid id, Stream content, string name, string? type, long length, CancellationToken ct)
    {
        var h = await db.Homework.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Homework", id);
        await scope.EnsureTeachesAsync(h.ClassId, h.SubjectId, ct);
        var file = await files.UploadAsync(h.SchoolId, content, name, type, length, ct);
        h.AttachmentId = file.Id;
        await db.SaveChangesAsync(ct);
        return FileService.ToDto(file)!;
    }

    /// <summary>A student hands in (or replaces, until graded) their work. Late hand-ins are flagged.</summary>
    public async Task SubmitAsync(Guid id, string? text, (Stream Content, string Name, string? Type, long Length)? upload, CancellationToken ct)
    {
        if (scope.Role != RoleType.Student) throw new ForbiddenException("Only students can submit homework.");
        var studentId = (await scope.MyStudentIdsAsync(ct)).FirstOrDefault();
        if (studentId == Guid.Empty) throw new ForbiddenException("Your account is not linked to a student record.");

        var h = await db.Homework.FirstOrDefaultAsync(x => x.Id == id && x.IsPublished, ct) ?? throw new NotFoundException("Homework", id);
        if (!await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.ClassId == h.ClassId && e.Status == EnrollmentStatus.Active, ct))
            throw new NotFoundException("Homework", id);
        if (string.IsNullOrWhiteSpace(text) && upload is null)
            throw new ValidationException("text", "Write an answer or attach a file.");

        var now = DateTime.UtcNow;
        var late = now > h.DueAtUtc;
        if (late && !h.AllowLateSubmission)
            throw new ValidationException("due", "The deadline has passed and late submissions are not accepted.");

        var sub = await db.HomeworkSubmissions.FirstOrDefaultAsync(s => s.HomeworkId == id && s.StudentId == studentId, ct);
        if (sub is { Status: not SubmissionStatus.Submitted })
            throw new ValidationException("status", "This homework has already been graded.");
        if (sub is null)
        {
            sub = new HomeworkSubmission { SchoolId = h.SchoolId, HomeworkId = id, StudentId = studentId };
            db.HomeworkSubmissions.Add(sub);
        }
        sub.SubmittedAtUtc = now;
        sub.IsLate = late;
        sub.Text = text?.Trim();
        if (upload is { } u)
            sub.AttachmentId = (await files.UploadAsync(h.SchoolId, u.Content, u.Name, u.Type, u.Length, ct)).Id;
        await db.SaveChangesAsync(ct);
    }

    public async Task GradeAsync(Guid submissionId, GradeSubmissionDto dto, CancellationToken ct)
    {
        var sub = await db.HomeworkSubmissions.Include(s => s.Homework).FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw new NotFoundException("Submission", submissionId);
        await scope.EnsureTeachesAsync(sub.Homework!.ClassId, sub.Homework.SubjectId, ct);
        if (dto.Score is { } score && sub.Homework.MaxScore is { } max && score > max)
            throw new ValidationException(nameof(dto.Score), $"The score cannot be above {max:0.##}.");
        sub.Score = dto.Score;
        sub.Feedback = dto.Feedback?.Trim();
        sub.Status = SubmissionStatus.Graded;
        sub.GradedAtUtc = DateTime.UtcNow;
        sub.GradedBy = current.UserId;
        await db.SaveChangesAsync(ct);
    }
}

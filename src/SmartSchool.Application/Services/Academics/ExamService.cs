using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services.Academics;

public class ExamService(IAppDbContext db, AccessScope scope, SchoolContext ctx, FileService files, NotificationService notifications)
{
    public async Task<List<ExamDto>> ListAsync(Guid? classId, Guid? subjectId, Guid? termId, Guid? studentId, CancellationToken ct)
    {
        var classesInYear = await ctx.ClassesInActiveYearAsync(ct);
        var q = db.Exams.AsNoTracking().Where(e => classesInYear.Any(c => c.Id == e.ClassId));
        var visible = await scope.VisibleClassIdsAsync(ct);
        if (visible is not null) q = q.Where(e => visible.Contains(e.ClassId));

        Guid? me = null;
        if (scope.IsFamily)
        {
            var mine = await scope.MyStudentIdsAsync(ct);
            me = studentId is { } s && mine.Contains(s) ? s : mine.FirstOrDefault();
            var childClasses = await db.Enrollments.Where(e => e.StudentId == me && e.Status == EnrollmentStatus.Active).Select(e => e.ClassId).ToListAsync(ct);
            q = q.Where(e => childClasses.Contains(e.ClassId));
        }
        if (classId is not null) q = q.Where(e => e.ClassId == classId);
        if (subjectId is not null) q = q.Where(e => e.SubjectId == subjectId);
        if (termId is not null) q = q.Where(e => e.TermId == termId);

        var family = scope.IsFamily;
        return await q.OrderByDescending(e => e.Date).ThenBy(e => e.Subject!.Name).Select(e => new ExamDto
        {
            Id = e.Id,
            ClassId = e.ClassId,
            ClassName = e.Class!.Name,
            SubjectId = e.SubjectId,
            SubjectName = e.Subject!.Name,
            SubjectColor = e.Subject.Color,
            TermId = e.TermId,
            TermName = e.Term!.Name,
            Title = e.Title,
            Type = e.Type,
            Date = e.Date,
            MaxScore = e.MaxScore,
            Weight = e.Weight,
            ResultsPublished = e.ResultsPublished,
            ResultCount = e.Results.Count(r => !r.IsDeleted && (r.Score != null || r.IsAbsent)),
            Average = family && !e.ResultsPublished ? null : e.Results.Where(r => !r.IsDeleted && r.Score != null && !r.IsAbsent).Average(r => r.Score),
            MyScore = me == null || !e.ResultsPublished ? null : e.Results.Where(r => r.StudentId == me && !r.IsDeleted).Select(r => r.Score).FirstOrDefault(),
            MyAbsent = me == null || !e.ResultsPublished ? null : e.Results.Where(r => r.StudentId == me && !r.IsDeleted).Select(r => (bool?)r.IsAbsent).FirstOrDefault()
        }).ToListAsync(ct);
    }

    public async Task<ExamDetailDto> GetAsync(Guid id, Guid? studentId, CancellationToken ct)
    {
        var e = await db.Exams.AsNoTracking().Include(x => x.Class).Include(x => x.Subject).Include(x => x.Term).Include(x => x.Document)
            .Include(x => x.Results).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Exam", id);
        await scope.EnsureCanSeeClassAsync(e.ClassId, ct);

        var students = await scope.ActiveStudentsOf(e.ClassId).AsNoTracking()
            .Select(s => new { s.Id, s.StudentNo, Name = s.FirstName + " " + s.LastName }).ToListAsync(ct);
        if (scope.IsFamily)
        {
            var mine = await scope.MyStudentIdsAsync(ct);
            var who = studentId is { } s && mine.Contains(s) ? s : mine.FirstOrDefault();
            students = e.ResultsPublished ? students.Where(s => s.Id == who).ToList() : [];
        }

        var scores = e.Results.Where(r => !r.IsDeleted && r.Score != null && !r.IsAbsent).Select(r => r.Score!.Value).ToList();
        var showStats = !scope.IsFamily || e.ResultsPublished;
        return new ExamDetailDto
        {
            Id = e.Id, ClassId = e.ClassId, ClassName = e.Class!.Name, SubjectId = e.SubjectId, SubjectName = e.Subject!.Name,
            SubjectColor = e.Subject.Color, TermId = e.TermId, TermName = e.Term!.Name, Title = e.Title, Type = e.Type, Date = e.Date,
            MaxScore = e.MaxScore, Weight = e.Weight, ResultsPublished = e.ResultsPublished, Description = e.Description,
            Document = FileService.ToDto(e.Document),
            ResultCount = scores.Count,
            Average = showStats && scores.Count > 0 ? Math.Round(scores.Average(), 2) : null,
            Highest = showStats && scores.Count > 0 ? scores.Max() : null,
            Lowest = showStats && scores.Count > 0 ? scores.Min() : null,
            Results = students.Select(s =>
            {
                var r = e.Results.FirstOrDefault(x => x.StudentId == s.Id && !x.IsDeleted);
                return new ExamResultDto { StudentId = s.Id, StudentNo = s.StudentNo, StudentName = s.Name, Score = r?.Score, IsAbsent = r?.IsAbsent ?? false, Comment = r?.Comment };
            }).ToList()
        };
    }

    public async Task<Guid> SaveAsync(Guid? id, ExamEditDto dto, CancellationToken ct)
    {
        var classId = dto.ClassId ?? throw new ValidationException(nameof(dto.ClassId), "Select a class.");
        var subjectId = dto.SubjectId ?? throw new ValidationException(nameof(dto.SubjectId), "Select a subject.");
        await scope.EnsureTeachesAsync(classId, subjectId, ct);
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class", classId);

        var termId = dto.TermId ?? await db.Terms.Where(t => t.AcademicYearId == cls.AcademicYearId && t.StartDate <= dto.Date && t.EndDate >= dto.Date)
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (termId is null || !await db.Terms.AnyAsync(t => t.Id == termId && t.AcademicYearId == cls.AcademicYearId, ct))
            throw new ValidationException(nameof(dto.TermId), "The exam date is not inside a term of this class's academic year. Choose a term.");

        Exam exam;
        if (id is { } existing)
        {
            exam = await db.Exams.Include(x => x.Results).FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Exam", existing);
            await scope.EnsureTeachesAsync(exam.ClassId, exam.SubjectId, ct);
            if (exam.ClassId != classId && exam.Results.Any(r => !r.IsDeleted))
                throw new ValidationException(nameof(dto.ClassId), "Results have been entered; the class can no longer change.");
            if (exam.Results.Any(r => !r.IsDeleted && r.Score > dto.MaxScore))
                throw new ValidationException(nameof(dto.MaxScore), "Some scores are above this maximum.");
        }
        else
        {
            exam = new Exam { SchoolId = cls.SchoolId };
            db.Exams.Add(exam);
        }
        exam.ClassId = classId;
        exam.SubjectId = subjectId;
        exam.TermId = termId.Value;
        exam.Title = dto.Title.Trim();
        exam.Type = dto.Type;
        exam.Date = dto.Date;
        exam.MaxScore = dto.MaxScore;
        exam.Weight = dto.Weight;
        exam.Description = dto.Description?.Trim();
        await db.SaveChangesAsync(ct);
        return exam.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var exam = await db.Exams.Include(x => x.Results).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Exam", id);
        await scope.EnsureTeachesAsync(exam.ClassId, exam.SubjectId, ct);
        foreach (var r in exam.Results) db.ExamResults.Remove(r);
        db.Exams.Remove(exam);
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveResultsAsync(Guid id, List<ExamResultDto> results, CancellationToken ct)
    {
        var exam = await db.Exams.Include(x => x.Results).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Exam", id);
        await scope.EnsureTeachesAsync(exam.ClassId, exam.SubjectId, ct);
        var enrolled = await scope.ActiveStudentsOf(exam.ClassId).Select(s => s.Id).ToListAsync(ct);

        foreach (var row in results)
        {
            if (!enrolled.Contains(row.StudentId))
                throw new ValidationException("studentId", "A result belongs to a student who is not in this class.");
            if (row.Score is { } s && (s < 0 || s > exam.MaxScore))
                throw new ValidationException("score", $"{row.StudentName}: scores must be between 0 and {exam.MaxScore:0.##}.");

            var r = exam.Results.FirstOrDefault(x => x.StudentId == row.StudentId && !x.IsDeleted);
            if (r is null)
            {
                if (row.Score is null && !row.IsAbsent && string.IsNullOrWhiteSpace(row.Comment)) continue;
                r = new ExamResult { SchoolId = exam.SchoolId, ExamId = exam.Id, StudentId = row.StudentId };
                db.ExamResults.Add(r);
            }
            r.IsAbsent = row.IsAbsent;
            r.Score = row.IsAbsent ? null : row.Score;
            r.Comment = row.Comment?.Trim();
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task PublishAsync(Guid id, bool publish, CancellationToken ct)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Exam", id);
        await scope.EnsureTeachesAsync(exam.ClassId, exam.SubjectId, ct);
        var announce = publish && !exam.ResultsPublished;
        exam.ResultsPublished = publish;
        if (announce)
        {
            var className = await db.Classes.Where(c => c.Id == exam.ClassId).Select(c => c.Name).FirstAsync(ct);
            notifications.Queue(await notifications.ClassFamilyUserIdsAsync(exam.ClassId, true, true, ct), $"Results published: {exam.Title}",
                $"{className} · {exam.Date:yyyy-MM-dd}", $"exams/{exam.Id}", "See results",
                NotificationSeverity.Info, exam.TenantId, exam.SchoolId, $"Students and parents of {className}");
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<FileDto> AttachAsync(Guid id, Stream content, string name, string? type, long length, CancellationToken ct)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Exam", id);
        await scope.EnsureTeachesAsync(exam.ClassId, exam.SubjectId, ct);
        var file = await files.UploadAsync(exam.SchoolId, content, name, type, length, ct);
        exam.DocumentId = file.Id;
        await db.SaveChangesAsync(ct);
        return FileService.ToDto(file)!;
    }
}

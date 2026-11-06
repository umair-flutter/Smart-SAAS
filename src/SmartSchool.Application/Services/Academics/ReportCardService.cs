using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services.Academics;

/// <summary>
/// Report cards are computed live from exam results:
/// subject average = Σ(score / max × 20 × weight) / Σ weight over the term's exams (absences excluded);
/// overall average = Σ(subject average × coefficient) / Σ coefficient over subjects that have an average.
/// </summary>
public class ReportCardService(IAppDbContext db, ICurrentUser current, AccessScope scope, IReportCardPdf pdf, NotificationService notifications)
{
    public const decimal Scale = 20m;

    private sealed record SubjectInfo(Guid Id, string Name, decimal Coefficient, Guid? TeacherId, string? TeacherName);

    private sealed class Computed
    {
        public required SchoolClass Class { get; init; }
        public required Term Term { get; init; }
        public required string SchoolName { get; init; }
        public required List<Student> Students { get; init; }
        public required List<SubjectInfo> Subjects { get; init; }
        public Dictionary<(Guid Student, Guid Subject), decimal> SubjectAverages { get; } = [];
        public Dictionary<(Guid Student, Guid Subject), int> ExamCounts { get; } = [];
        public Dictionary<Guid, decimal> Overall { get; } = [];
        public Dictionary<Guid, int> Ranks { get; } = [];
        public Dictionary<Guid, decimal> SubjectClassAverages { get; } = [];
    }

    /// <summary>Terms of the class's academic year and the one that contains today (or the latest started).</summary>
    public async Task<Guid> ResolveTermAsync(Guid classId, Guid? termId, CancellationToken ct)
    {
        var yearId = await db.Classes.Where(c => c.Id == classId).Select(c => (Guid?)c.AcademicYearId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Class", classId);
        var terms = await db.Terms.Where(t => t.AcademicYearId == yearId).OrderBy(t => t.StartDate).ToListAsync(ct);
        if (termId is { } tid)
            return terms.Any(t => t.Id == tid) ? tid : throw new ValidationException("termId", "This term does not belong to the class's academic year.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return (terms.LastOrDefault(t => t.StartDate <= today) ?? terms.FirstOrDefault())?.Id
            ?? throw new ValidationException("termId", "The academic year has no terms.");
    }

    public async Task<List<ReportCardSummaryDto>> ListAsync(Guid classId, Guid? termId, CancellationToken ct)
    {
        if (scope.IsFamily) throw new ForbiddenException();
        await scope.EnsureTeachesAsync(classId, null, ct);
        var data = await ComputeAsync(classId, await ResolveTermAsync(classId, termId, ct), ct);
        var cards = await db.ReportCards.AsNoTracking().Where(r => r.ClassId == classId && r.TermId == data.Term.Id).ToListAsync(ct);
        var absences = await AbsencesAsync(data.Students.Select(s => s.Id).ToList(), data.Term, ct);

        return data.Students.Select(s =>
        {
            var card = cards.FirstOrDefault(c => c.StudentId == s.Id);
            return new ReportCardSummaryDto
            {
                StudentId = s.Id, StudentNo = s.StudentNo, StudentName = s.FullName,
                Average = data.Overall.TryGetValue(s.Id, out var a) ? a : null,
                Rank = data.Ranks.TryGetValue(s.Id, out var r) ? r : null,
                Absences = absences.TryGetValue(s.Id, out var ab) ? ab.Days : 0,
                Status = card?.Status ?? ReportCardStatus.Draft,
                HasGeneralComment = !string.IsNullOrWhiteSpace(card?.GeneralComment)
            };
        }).OrderBy(r => r.Rank ?? int.MaxValue).ThenBy(r => r.StudentName).ToList();
    }

    public async Task<ReportCardDto> GetAsync(Guid studentId, Guid? termId, CancellationToken ct)
    {
        await scope.EnsureCanSeeStudentAsync(studentId, ct);
        var classId = await db.Enrollments
            .Where(e => e.StudentId == studentId && e.Status != EnrollmentStatus.Withdrawn)
            .OrderByDescending(e => e.Class!.AcademicYear!.IsCurrent).ThenByDescending(e => e.Class!.AcademicYear!.StartDate)
            .Select(e => (Guid?)e.ClassId).FirstOrDefaultAsync(ct)
            ?? throw new ValidationException("class", "This student is not enrolled in a class.");
        if (termId is { } requested)
        {
            // A term of an earlier year points to the class the student was in that year.
            var yearOfTerm = await db.Terms.Where(t => t.Id == requested).Select(t => (Guid?)t.AcademicYearId).FirstOrDefaultAsync(ct);
            classId = await db.Enrollments.Where(e => e.StudentId == studentId && e.AcademicYearId == yearOfTerm).Select(e => (Guid?)e.ClassId).FirstOrDefaultAsync(ct) ?? classId;
        }

        var data = await ComputeAsync(classId, await ResolveTermAsync(classId, termId, ct), ct);
        var student = data.Students.FirstOrDefault(s => s.Id == studentId)
            ?? await db.Students.AsNoTracking().FirstAsync(s => s.Id == studentId, ct);
        var card = await db.ReportCards.AsNoTracking().Include(r => r.SubjectComments)
            .FirstOrDefaultAsync(r => r.StudentId == studentId && r.TermId == data.Term.Id, ct);

        if (scope.IsFamily && card?.Status != ReportCardStatus.Published)
            throw new ValidationException("status", $"The {data.Term.Name} report card has not been published yet.");

        var absences = (await AbsencesAsync([studentId], data.Term, ct)).GetValueOrDefault(studentId);
        var editable = await EditableSubjectsAsync(data, ct);

        return new ReportCardDto
        {
            StudentId = student.Id, StudentNo = student.StudentNo, StudentName = student.FullName, DateOfBirth = student.DateOfBirth,
            SchoolName = data.SchoolName, ClassId = data.Class.Id, ClassName = data.Class.Name, AcademicYearName = data.Class.AcademicYear!.Name,
            TermId = data.Term.Id, TermName = data.Term.Name, HomeroomTeacher = data.Class.HomeroomTeacher?.FullName,
            Subjects = data.Subjects.Select(sub => new ReportCardSubjectDto
            {
                SubjectId = sub.Id, SubjectName = sub.Name, Coefficient = sub.Coefficient, TeacherName = sub.TeacherName,
                Average = data.SubjectAverages.TryGetValue((studentId, sub.Id), out var avg) ? avg : null,
                ClassAverage = data.SubjectClassAverages.TryGetValue(sub.Id, out var ca) ? ca : null,
                ExamCount = data.ExamCounts.GetValueOrDefault((studentId, sub.Id)),
                Comment = card?.SubjectComments.FirstOrDefault(c => c.SubjectId == sub.Id)?.Comment
            }).ToList(),
            Average = data.Overall.TryGetValue(studentId, out var o) ? o : null,
            Rank = data.Ranks.TryGetValue(studentId, out var rank) ? rank : null,
            ClassSize = data.Students.Count,
            ClassAverage = data.Overall.Count > 0 ? Math.Round(data.Overall.Values.Average(), 2) : null,
            AbsenceDays = absences.Days,
            UnjustifiedAbsences = absences.Unjustified,
            LateCount = absences.Late,
            GeneralComment = card?.GeneralComment,
            Status = card?.Status ?? ReportCardStatus.Draft,
            PublishedAtUtc = card?.PublishedAtUtc,
            CanEditGeneral = !scope.IsFamily && card?.Status != ReportCardStatus.Published
                && current.HasPermission(Shared.Security.Permissions.ReportCards.Manage)
                && (scope.IsAdmin || await scope.IsHomeroomTeacherAsync(data.Class.Id, ct)),
            EditableSubjectIds = card?.Status == ReportCardStatus.Published ? [] : editable
        };
    }

    public async Task SaveCommentsAsync(Guid studentId, Guid termId, ReportCardCommentsDto dto, CancellationToken ct)
    {
        if (scope.IsFamily) throw new ForbiddenException();
        var card = await GetAsync(studentId, termId, ct);
        if (card.Status == ReportCardStatus.Published)
            throw new ValidationException("status", "Unpublish the report card before changing comments.");

        var entity = await db.ReportCards.Include(r => r.SubjectComments).FirstOrDefaultAsync(r => r.StudentId == studentId && r.TermId == card.TermId, ct);
        if (entity is null)
        {
            var schoolId = await db.Classes.Where(c => c.Id == card.ClassId).Select(c => c.SchoolId).FirstAsync(ct);
            entity = new ReportCard { SchoolId = schoolId, StudentId = studentId, ClassId = card.ClassId, TermId = card.TermId };
            db.ReportCards.Add(entity);
        }

        if (dto.GeneralComment is not null)
        {
            if (!card.CanEditGeneral) throw new ForbiddenException("Only the homeroom teacher or an administrator can write the general comment.");
            entity.GeneralComment = dto.GeneralComment.Trim();
        }

        var teacherId = await scope.TeacherIdAsync(ct);
        foreach (var s in dto.Subjects)
        {
            if (!card.EditableSubjectIds.Contains(s.SubjectId))
                throw new ForbiddenException("You can only comment on the subjects you teach in this class.");
            var existing = entity.SubjectComments.FirstOrDefault(c => c.SubjectId == s.SubjectId);
            if (string.IsNullOrWhiteSpace(s.Comment))
            {
                if (existing is not null) entity.SubjectComments.Remove(existing);
                continue;
            }
            if (existing is null)
            {
                existing = new ReportCardComment { ReportCardId = entity.Id, SubjectId = s.SubjectId };
                entity.SubjectComments.Add(existing);
            }
            existing.Comment = s.Comment.Trim();
            existing.TeacherId = teacherId;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Publishes (or withdraws) every report card of a class for a term. Administrators only.</summary>
    public async Task<int> PublishAsync(Guid classId, Guid? termId, bool publish, CancellationToken ct)
    {
        if (!scope.IsAdmin) throw new ForbiddenException("Only school administrators can publish report cards.");
        var tid = await ResolveTermAsync(classId, termId, ct);
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class", classId);
        var students = await scope.ActiveStudentsOf(classId).Select(s => s.Id).ToListAsync(ct);
        var cards = await db.ReportCards.Where(r => r.ClassId == classId && r.TermId == tid).ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var sid in students)
        {
            var card = cards.FirstOrDefault(c => c.StudentId == sid);
            if (card is null)
            {
                card = new ReportCard { SchoolId = cls.SchoolId, StudentId = sid, ClassId = classId, TermId = tid };
                db.ReportCards.Add(card);
            }
            var announce = publish && card.Status != ReportCardStatus.Published;
            card.Status = publish ? ReportCardStatus.Published : ReportCardStatus.Draft;
            card.PublishedAtUtc = publish ? now : null;
            if (announce)
            {
                var termName = await db.Terms.Where(t => t.Id == tid).Select(t => t.Name).FirstAsync(ct);
                notifications.Queue(await notifications.FamilyUserIdsAsync([sid], true, true, ct), $"Report card available: {termName}",
                    $"The {termName} report card for {cls.Name} has been published.", $"report-cards/{sid}?termId={tid}", "Open report card",
                    NotificationSeverity.Success, cls.TenantId, cls.SchoolId, $"Family of a student in {cls.Name}");
            }
        }
        await db.SaveChangesAsync(ct);
        return students.Count;
    }

    public async Task<(byte[] Content, string FileName)> PdfAsync(Guid studentId, Guid? termId, string language, CancellationToken ct)
    {
        var card = await GetAsync(studentId, termId, ct);
        var lang = language is "fr" or "ar" ? language : "en";
        var bytes = pdf.Render(card, lang);
        var safe = string.Concat(card.StudentName.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');
        return (bytes, $"report-card-{safe}-{card.TermName.Replace(' ', '-')}-{lang}.pdf");
    }

    // ---------- computation ----------

    private async Task<Computed> ComputeAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var cls = await db.Classes.AsNoTracking().Include(c => c.AcademicYear).Include(c => c.HomeroomTeacher)
            .FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class", classId);
        var term = await db.Terms.AsNoTracking().FirstAsync(t => t.Id == termId, ct);
        var schoolName = await db.Schools.Where(s => s.Id == cls.SchoolId).Select(s => s.Name).FirstAsync(ct);
        var students = await scope.ActiveStudentsOf(classId).AsNoTracking().ToListAsync(ct);

        var exams = await db.Exams.AsNoTracking().Include(e => e.Results)
            .Where(e => e.ClassId == classId && e.TermId == termId).ToListAsync(ct);
        var assigned = await db.TeachingAssignments.AsNoTracking().Where(a => a.ClassId == classId)
            .Select(a => new SubjectInfo(a.SubjectId, a.Subject!.Name, a.Subject.Coefficient, a.TeacherId, a.Teacher!.FirstName + " " + a.Teacher.LastName))
            .ToListAsync(ct);
        var extraIds = exams.Select(e => e.SubjectId).Distinct().Where(id => assigned.All(a => a.Id != id)).ToList();
        var extra = await db.Subjects.AsNoTracking().Where(s => extraIds.Contains(s.Id))
            .Select(s => new SubjectInfo(s.Id, s.Name, s.Coefficient, null, null)).ToListAsync(ct);

        var data = new Computed
        {
            Class = cls, Term = term, SchoolName = schoolName, Students = students,
            Subjects = assigned.Concat(extra).OrderBy(s => s.Name).ToList()
        };

        foreach (var student in students)
        {
            decimal weighted = 0, coefSum = 0;
            foreach (var subject in data.Subjects)
            {
                decimal sum = 0, weights = 0;
                var count = 0;
                foreach (var exam in exams.Where(e => e.SubjectId == subject.Id && e.MaxScore > 0))
                {
                    var r = exam.Results.FirstOrDefault(x => x.StudentId == student.Id && !x.IsDeleted);
                    if (r?.Score is not { } score || r.IsAbsent) continue;
                    sum += score / exam.MaxScore * Scale * exam.Weight;
                    weights += exam.Weight;
                    count++;
                }
                data.ExamCounts[(student.Id, subject.Id)] = count;
                if (weights == 0) continue;
                var avg = Math.Round(sum / weights, 2, MidpointRounding.AwayFromZero);
                data.SubjectAverages[(student.Id, subject.Id)] = avg;
                weighted += avg * subject.Coefficient;
                coefSum += subject.Coefficient;
            }
            if (coefSum > 0) data.Overall[student.Id] = Math.Round(weighted / coefSum, 2, MidpointRounding.AwayFromZero);
        }

        // Competition ranking: equal averages share a rank.
        var ordered = data.Overall.OrderByDescending(x => x.Value).ToList();
        for (var i = 0; i < ordered.Count; i++)
            data.Ranks[ordered[i].Key] = i > 0 && ordered[i].Value == ordered[i - 1].Value ? data.Ranks[ordered[i - 1].Key] : i + 1;

        foreach (var subject in data.Subjects)
        {
            var values = data.SubjectAverages.Where(x => x.Key.Subject == subject.Id).Select(x => x.Value).ToList();
            if (values.Count > 0) data.SubjectClassAverages[subject.Id] = Math.Round(values.Average(), 2);
        }
        return data;
    }

    private async Task<Dictionary<Guid, (int Days, int Unjustified, int Late)>> AbsencesAsync(List<Guid> studentIds, Term term, CancellationToken ct)
    {
        var rows = await db.AttendanceRecords.AsNoTracking()
            .Where(r => studentIds.Contains(r.StudentId) && r.Date >= term.StartDate && r.Date <= term.EndDate && r.Status != AttendanceStatus.Present)
            .Select(r => new { r.StudentId, r.Status, Excused = r.Reason != null && r.Reason.IsExcused })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => (
            g.Count(r => r.Status is AttendanceStatus.Absent or AttendanceStatus.Excused),
            g.Count(r => r.Status == AttendanceStatus.Absent && !r.Excused),
            g.Count(r => r.Status == AttendanceStatus.Late)));
    }

    private async Task<List<Guid>> EditableSubjectsAsync(Computed data, CancellationToken ct)
    {
        if (scope.IsFamily || !current.HasPermission(Shared.Security.Permissions.ReportCards.Manage)) return [];
        if (scope.IsAdmin) return data.Subjects.Select(s => s.Id).ToList();
        var tid = await scope.TeacherIdAsync(ct);
        return data.Subjects.Where(s => s.TeacherId == tid).Select(s => s.Id).ToList();
    }
}

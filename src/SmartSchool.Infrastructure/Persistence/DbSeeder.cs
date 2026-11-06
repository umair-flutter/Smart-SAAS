using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSchool.Application.Common;
using SmartSchool.Application.Security;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Infrastructure.Persistence;

public class SeedSettings
{
    public const string Section = "Seed";

    /// <summary>Create the demo tenants and one account per role on first start.</summary>
    public bool CreateDemoData { get; set; } = true;

    /// <summary>Show the quick-login role buttons on the sign-in page. Turn off in production.</summary>
    public bool ShowDemoAccounts { get; set; }

    public string SuperAdminEmail { get; set; } = "superadmin@smartschool.local";
    public string SuperAdminPassword { get; set; } = string.Empty;
    public string DemoPassword { get; set; } = string.Empty;
}

/// <summary>Creates the system roles, the SuperAdmin account and optional demo data. Safe to run on every start.</summary>
public class DbSeeder(AppDbContext db, IPasswordService passwords, IOptions<SeedSettings> options, ILogger<DbSeeder> logger)
{
    private readonly SeedSettings _s = options.Value;

    public static readonly (RoleType Type, string Email)[] DemoUsers =
    [
        (RoleType.TenantAdmin, "admin@demo-academy.local"),
        (RoleType.SchoolAdmin, "schooladmin@demo-academy.local"),
        (RoleType.Teacher, "teacher@demo-academy.local"),
        (RoleType.Parent, "parent@demo-academy.local"),
        (RoleType.Student, "student@demo-academy.local")
    ];

    public List<DemoAccount> GetDemoAccounts()
    {
        if (!_s.ShowDemoAccounts) return [];
        var list = new List<DemoAccount> { new("SuperAdmin", "SA", _s.SuperAdminEmail, _s.SuperAdminPassword) };
        list.AddRange(DemoUsers.Select(d => new DemoAccount(d.Type.DisplayName(), Initials(d.Type), d.Email, _s.DemoPassword)));
        return list;
    }

    private static string Initials(RoleType t) => t switch
    {
        RoleType.TenantAdmin => "TA",
        RoleType.SchoolAdmin => "SC",
        RoleType.Teacher => "TE",
        RoleType.Parent => "PA",
        RoleType.Student => "ST",
        _ => "SA"
    };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        var roles = await EnsureSystemRoles(ct);
        await EnsureSuperAdmin(roles[RoleType.SuperAdmin], ct);

        var plans = await EnsurePlans(ct);

        if (_s.CreateDemoData && !await db.Tenants.IgnoreQueryFilters().AnyAsync(ct))
            await CreateDemoData(roles, ct);

        if (_s.CreateDemoData && !await db.Subscriptions.IgnoreQueryFilters().AnyAsync(ct))
            await CreateDemoBilling(plans, ct);

        if (_s.CreateDemoData && !await db.AcademicYears.IgnoreQueryFilters().AnyAsync(ct))
            await CreateDemoSchool(ct);

        if (_s.CreateDemoData && !await db.AbsenceReasons.IgnoreQueryFilters().AnyAsync(ct))
            await CreateDemoAcademics(ct);

        if (_s.CreateDemoData && !await db.StatusOptions.IgnoreQueryFilters().AnyAsync(ct))
            await CreateDemoStatusesAndAnnouncement(ct);
    }

    private async Task CreateDemoStatusesAndAnnouncement(CancellationToken ct)
    {
        var demo = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Code == "demo-academy" && !t.IsDeleted, ct);
        if (demo is null) return;

        StatusOption S(StatusCategory c, string name, string color, int order) => new() { TenantId = demo.Id, Category = c, Name = name, Color = color, SortOrder = order };
        var scholarship = S(StatusCategory.Student, "Scholarship", "#2563eb", 2);
        db.StatusOptions.AddRange(
            S(StatusCategory.Student, "Regular", "#16a34a", 1), scholarship,
            S(StatusCategory.Student, "Repeating the year", "#ea580c", 3), S(StatusCategory.Student, "Learning support", "#7c3aed", 4),
            S(StatusCategory.Teacher, "Full-time", "#16a34a", 1), S(StatusCategory.Teacher, "Part-time", "#0891b2", 2), S(StatusCategory.Teacher, "On leave", "#dc2626", 3),
            S(StatusCategory.Guardian, "Parents' association", "#ca8a04", 1));

        var student = await db.Students.IgnoreQueryFilters().Where(s => s.TenantId == demo.Id && s.FirstName == "Nour" && !s.IsDeleted).FirstOrDefaultAsync(ct);
        if (student is not null) student.StatusOptionId = scholarship.Id;

        var users = await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == demo.Id && !u.IsDeleted).Select(u => u.Id).ToListAsync(ct);
        var welcome = new Notification
        {
            TenantId = demo.Id, Title = "Welcome to SmartSchool", Kind = NotificationKind.Broadcast, Severity = NotificationSeverity.Success,
            Body = "Your school workspace is ready. Use the bell at the top of the page to see messages from your school.",
            SenderName = "Platform Owner", AudienceLabel = "Everyone in the organization", Link = "", ActionLabel = null
        };
        welcome.Recipients.AddRange(users.Select(id => new NotificationRecipient { NotificationId = welcome.Id, UserId = id }));
        db.Notifications.Add(welcome);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo status options and announcement created");
    }

    /// <summary>Attendance, homework, exams and a published report card for Grade 5 A of Central Campus.</summary>
    private async Task CreateDemoAcademics(CancellationToken ct)
    {
        var school = await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "CENTRAL" && !s.IsDeleted, ct);
        if (school is null) return;
        Guid tid = school.TenantId, sid = school.Id;

        AbsenceReason R(string name, bool excused, bool doc) => new() { TenantId = tid, SchoolId = sid, Name = name, IsExcused = excused, RequiresDocument = doc };
        var illness = R("Illness", true, true);
        var family = R("Family reason", true, false);
        var medical = R("Medical appointment", true, true);
        var transport = R("Transport problem", false, false);
        var unjustified = R("Unjustified", false, false);
        db.AbsenceReasons.AddRange(illness, family, medical, transport, unjustified);

        var cls = await db.Classes.IgnoreQueryFilters().Include(c => c.AcademicYear)
            .FirstOrDefaultAsync(c => c.SchoolId == sid && c.Name == "Grade 5 A" && !c.IsDeleted, ct);
        if (cls is null) { await db.SaveChangesAsync(ct); return; }

        var students = await db.Students.IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.Enrollments.Any(e => e.ClassId == cls.Id && !e.IsDeleted)).OrderBy(s => s.StudentNo).ToListAsync(ct);
        var terms = await db.Terms.IgnoreQueryFilters().Where(t => t.AcademicYearId == cls.AcademicYearId && !t.IsDeleted).OrderBy(t => t.StartDate).ToListAsync(ct);
        var assignments = await db.TeachingAssignments.IgnoreQueryFilters().Include(a => a.Subject)
            .Where(a => a.ClassId == cls.Id && !a.IsDeleted).ToListAsync(ct);
        if (students.Count == 0 || terms.Count == 0 || assignments.Count == 0) { await db.SaveChangesAsync(ct); return; }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var term = terms.LastOrDefault(t => t.StartDate <= today) ?? terms[0];
        var nour = students.FirstOrDefault(s => s.FirstName == "Nour") ?? students[0];
        var rng = new Random(42);

        // Attendance: the last ten school days inside the term.
        var day = today.AddDays(-1);
        var days = new List<DateOnly>();
        while (days.Count < 10 && day >= term.StartDate)
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days.Add(day);
            day = day.AddDays(-1);
        }
        foreach (var (date, i) in days.Select((d, i) => (d, i)))
            foreach (var st in students)
            {
                var roll = rng.Next(100);
                var status = roll < 88 ? AttendanceStatus.Present : roll < 94 ? AttendanceStatus.Late : AttendanceStatus.Absent;
                if (st.Id == nour.Id) status = i == 3 ? AttendanceStatus.Absent : i == 6 ? AttendanceStatus.Late : AttendanceStatus.Present;
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    TenantId = tid, SchoolId = sid, ClassId = cls.Id, StudentId = st.Id, Date = date, Status = status,
                    ArrivalTime = status == AttendanceStatus.Late ? new TimeOnly(8, 40 + rng.Next(15)) : null,
                    ReasonId = status == AttendanceStatus.Absent ? (st.Id == nour.Id ? illness.Id : rng.Next(2) == 0 ? unjustified.Id : null) : null,
                    Note = st.Id == nour.Id && status == AttendanceStatus.Absent ? "Fever, doctor's note to follow" : null,
                    IsValidated = i > 0
                });
            }

        // Homework: one past and graded, two upcoming.
        var now = DateTime.UtcNow;
        TeachingAssignment A(string code) => assignments.FirstOrDefault(a => a.Subject!.Code == code) ?? assignments[0];
        Homework H(TeachingAssignment a, string title, string text, DateTime assigned, DateTime due) => new()
        {
            TenantId = tid, SchoolId = sid, ClassId = cls.Id, SubjectId = a.SubjectId, TeacherId = a.TeacherId, Title = title, Instructions = text,
            AssignedAtUtc = assigned, DueAtUtc = due, MaxScore = 20, AllowLateSubmission = true, IsPublished = true
        };
        var math = H(A("MATH"), "Fractions worksheet", "Complete exercises 1 to 12 on page 48. Show every step of your working.", now.AddDays(-10), now.Date.AddDays(-3).AddHours(17));
        var french = H(A("FR"), "Book report: Le Petit Prince", "Write one page about your favourite chapter and explain why you chose it.", now.AddDays(-2), now.Date.AddDays(5).AddHours(17));
        var science = H(A("SCI"), "Plant growth observation", "Measure your bean plant every two days and record the results in a table.", now.AddDays(-1), now.Date.AddDays(9).AddHours(17));
        db.Homework.AddRange(math, french, science);
        foreach (var (st, i) in students.Select((s, i) => (s, i)))
        {
            if (i % 5 == 4) continue; // a few students have not handed in
            var late = i % 4 == 3;
            var graded = i % 3 != 2;
            db.HomeworkSubmissions.Add(new HomeworkSubmission
            {
                TenantId = tid, SchoolId = sid, HomeworkId = math.Id, StudentId = st.Id,
                SubmittedAtUtc = late ? math.DueAtUtc.AddHours(20) : math.DueAtUtc.AddDays(-1), IsLate = late,
                Text = "All twelve exercises completed.", Status = graded ? SubmissionStatus.Graded : SubmissionStatus.Submitted,
                Score = graded ? 11 + rng.Next(9) : null, Feedback = graded ? "Good work, check exercise 9 again." : null,
                GradedAtUtc = graded ? now.AddDays(-1) : null
            });
        }
        db.HomeworkSubmissions.Add(new HomeworkSubmission
        {
            TenantId = tid, SchoolId = sid, HomeworkId = french.Id, StudentId = students[(students.IndexOf(nour) + 1) % students.Count].Id,
            SubmittedAtUtc = now.AddHours(-5), Text = "My favourite chapter is the one with the fox…"
        });

        // Exams for the current term: two per subject, results for everyone, published.
        var examDate = term.StartDate.AddDays(20);
        foreach (var a in assignments)
        {
            foreach (var (title, type, max, weight, offset) in new[] { ("Quiz 1", ExamType.Quiz, 10m, 1m, 0), ("Unit test", ExamType.Test, 20m, 2m, 15) })
            {
                var date = examDate.AddDays(offset);
                if (date > today) date = today.AddDays(-1);
                var exam = new Exam
                {
                    TenantId = tid, SchoolId = sid, ClassId = cls.Id, SubjectId = a.SubjectId, TermId = term.Id,
                    Title = $"{a.Subject!.Name}: {title}", Type = type, Date = date, MaxScore = max, Weight = weight, ResultsPublished = true
                };
                db.Exams.Add(exam);
                foreach (var st in students)
                {
                    var absent = rng.Next(30) == 0 && st.Id != nour.Id;
                    var ability = 0.45 + ((st.StudentNo[^1] - '0') % 8) / 20.0 + (st.Id == nour.Id ? 0.2 : 0);
                    var score = Math.Clamp(Math.Round((decimal)(ability + rng.NextDouble() * 0.25) * max * 2, MidpointRounding.AwayFromZero) / 2, 0, max);
                    db.ExamResults.Add(new ExamResult
                    {
                        TenantId = tid, SchoolId = sid, ExamId = exam.Id, StudentId = st.Id, IsAbsent = absent, Score = absent ? null : score
                    });
                }
            }
        }

        // Published report card with comments for the demo student, drafts for the others.
        foreach (var st in students)
        {
            var card = new ReportCard
            {
                TenantId = tid, SchoolId = sid, StudentId = st.Id, ClassId = cls.Id, TermId = term.Id,
                Status = ReportCardStatus.Published, PublishedAtUtc = now,
                GeneralComment = st.Id == nour.Id ? "A serious and curious student who takes part well in class. Keep it up!" : null
            };
            if (st.Id == nour.Id)
                foreach (var a in assignments)
                    card.SubjectComments.Add(new ReportCardComment
                    {
                        ReportCardId = card.Id, SubjectId = a.SubjectId, TeacherId = a.TeacherId,
                        Comment = a.Subject!.Code switch { "MATH" => "Very good reasoning.", "FR" => "Rich vocabulary, careful with spelling.", "PE" => "Excellent team spirit.", _ => "Good work this term." }
                    });
            db.ReportCards.Add(card);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo academics created for {Class}", cls.Name);
    }

    /// <summary>Fills Central Campus of Demo Academy with a realistic school year.</summary>
    private async Task CreateDemoSchool(CancellationToken ct)
    {
        var school = await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "CENTRAL" && !s.IsDeleted, ct);
        if (school is null) return;
        Guid tid = school.TenantId, sid = school.Id;
        var users = await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == tid).ToDictionaryAsync(u => u.Email, ct);
        Guid? UserId(string email) => users.TryGetValue(email, out var u) ? u.Id : null;

        var startYear = DateTime.UtcNow.Month >= 8 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;
        var year = new AcademicYear
        {
            TenantId = tid, SchoolId = sid, Name = $"{startYear}-{startYear + 1}", IsCurrent = true,
            StartDate = new DateOnly(startYear, 9, 1), EndDate = new DateOnly(startYear + 1, 6, 30)
        };
        year.Terms.AddRange(
        [
            new Term { TenantId = tid, SchoolId = sid, AcademicYearId = year.Id, Name = "Term 1", StartDate = new(startYear, 9, 1), EndDate = new(startYear, 12, 20), SortOrder = 0 },
            new Term { TenantId = tid, SchoolId = sid, AcademicYearId = year.Id, Name = "Term 2", StartDate = new(startYear + 1, 1, 5), EndDate = new(startYear + 1, 3, 28), SortOrder = 1 },
            new Term { TenantId = tid, SchoolId = sid, AcademicYearId = year.Id, Name = "Term 3", StartDate = new(startYear + 1, 4, 13), EndDate = new(startYear + 1, 6, 30), SortOrder = 2 }
        ]);
        db.AcademicYears.Add(year);

        Subject S(string name, string code, decimal coef, string color) =>
            new() { TenantId = tid, SchoolId = sid, Name = name, Code = code, Coefficient = coef, Color = color };
        var subjects = new[]
        {
            S("Mathematics", "MATH", 4, "#2563eb"), S("French", "FR", 3, "#db2777"), S("Arabic", "AR", 3, "#059669"),
            S("English", "EN", 2, "#7c3aed"), S("Science", "SCI", 2, "#ea580c"), S("History & Geography", "HG", 2, "#ca8a04"),
            S("Physical Education", "PE", 1, "#0891b2")
        };
        db.Subjects.AddRange(subjects);

        var rooms = Enumerable.Range(1, 5).Select(i => new Room { TenantId = tid, SchoolId = sid, Code = $"R10{i}", Name = $"Room 10{i}", Building = "Main", Capacity = 32, Type = "Classroom" }).ToList();
        rooms.Add(new Room { TenantId = tid, SchoolId = sid, Code = "GYM", Name = "Gymnasium", Building = "Sports hall", Capacity = 60, Type = "Gym" });
        db.Rooms.AddRange(rooms);

        Teacher T(string first, string last, string specialty, string? email = null) => new()
        {
            TenantId = tid, SchoolId = sid, FirstName = first, LastName = last, Specialty = specialty,
            Email = email ?? $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@demo-academy.local",
            EmployeeNo = $"T{Random.Shared.Next(1000, 9999)}", HireDate = new DateOnly(startYear - Random.Shared.Next(1, 10), 9, 1),
            UserId = email is null ? null : UserId(email)
        };
        var teachers = new[]
        {
            T("Leila", "Fassi", "Mathematics", "teacher@demo-academy.local"), T("Hamza", "Idrissi", "French"),
            T("Salma", "Chraibi", "Arabic"), T("David", "Miller", "English"), T("Rania", "Bennis", "Science"), T("Mehdi", "Kettani", "Physical Education")
        };
        db.Teachers.AddRange(teachers);

        var classes = new[] { ("Grade 5 A", "Grade 5"), ("Grade 5 B", "Grade 5"), ("Grade 6 A", "Grade 6"), ("Grade 6 B", "Grade 6") }
            .Select((c, i) => new SchoolClass
            {
                TenantId = tid, SchoolId = sid, AcademicYearId = year.Id, Name = c.Item1, Level = c.Item2, Capacity = 30,
                RoomId = rooms[i].Id, HomeroomTeacherId = teachers[i].Id
            }).ToList();
        db.Classes.AddRange(classes);

        // subject index -> teacher index
        int[] teacherFor = [0, 1, 2, 3, 4, 2, 5];
        foreach (var cls in classes)
            for (var i = 0; i < subjects.Length; i++)
                db.TeachingAssignments.Add(new TeachingAssignment
                {
                    TenantId = tid, SchoolId = sid, ClassId = cls.Id, SubjectId = subjects[i].Id, TeacherId = teachers[teacherFor[i]].Id,
                    HoursPerWeek = subjects[i].Coefficient + 1
                });

        string[] firsts = ["Adam", "Yasmine", "Rayan", "Ines", "Ilyas", "Lina", "Youssef", "Malak", "Anas", "Hiba", "Omar", "Sara"];
        string[] lasts = ["Alami", "Berrada", "Chami", "El Amrani", "Fikri", "Ghazi", "Haddou", "Jabri", "Lahlou", "Mernissi", "Naciri", "Ouazzani"];
        var students = new List<Student>();
        for (var i = 0; i < 24; i++)
        {
            var isDemo = i == 0;
            var st = new Student
            {
                TenantId = tid, SchoolId = sid, StudentNo = $"S{startYear % 100:D2}{i + 1:D4}",
                FirstName = isDemo ? "Nour" : firsts[i % firsts.Length], LastName = isDemo ? "Tazi" : lasts[(i * 7 + i / firsts.Length) % lasts.Length],
                Gender = i % 2 == 0 ? Gender.Female : Gender.Male,
                DateOfBirth = new DateOnly(startYear - 11 + (i % 2), 1 + i % 12, 1 + i % 27),
                AdmissionDate = year.StartDate,
                Email = isDemo ? "student@demo-academy.local" : null, UserId = isDemo ? UserId("student@demo-academy.local") : null
            };
            students.Add(st);
            db.Enrollments.Add(new Enrollment
            {
                TenantId = tid, SchoolId = sid, StudentId = st.Id, ClassId = classes[i % classes.Count].Id, AcademicYearId = year.Id, EnrolledOn = year.StartDate
            });
        }
        db.Students.AddRange(students);

        var parent = new Guardian
        {
            TenantId = tid, SchoolId = sid, FirstName = "Omar", LastName = "Tazi", Email = "parent@demo-academy.local", Phone = "+212 600 000 001",
            Occupation = "Engineer", UserId = UserId("parent@demo-academy.local")
        };
        var other = new Guardian { TenantId = tid, SchoolId = sid, FirstName = "Khadija", LastName = "Alami", Phone = "+212 600 000 002", Occupation = "Doctor" };
        db.Guardians.AddRange(parent, other);
        db.StudentGuardians.Add(new StudentGuardian { StudentId = students[0].Id, GuardianId = parent.Id, Relationship = "Father", IsPrimary = true });
        db.StudentGuardians.Add(new StudentGuardian { StudentId = students[1].Id, GuardianId = other.Id, Relationship = "Mother", IsPrimary = true });

        // A full week for Grade 5 A (Nour's class): four lessons a day, Monday to Friday.
        var g5a = classes[0];
        TimeOnly[] starts = [new(8, 30), new(9, 30), new(10, 45), new(11, 45)];
        var rotation = 0;
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            foreach (var start in starts)
            {
                var si = rotation++ % subjects.Length;
                db.TimetableSlots.Add(new TimetableSlot
                {
                    TenantId = tid, SchoolId = sid, ClassId = g5a.Id, DayOfWeek = day, StartTime = start, EndTime = start.AddMinutes(55),
                    SubjectId = subjects[si].Id, TeacherId = teachers[teacherFor[si]].Id, RoomId = si == 6 ? rooms[5].Id : g5a.RoomId
                });
            }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo school data created for {School}", school.Name);
    }

    /// <summary>Creates the three default plans on first start. Existing plans are never changed.</summary>
    private async Task<Dictionary<string, Plan>> EnsurePlans(CancellationToken ct)
    {
        var existing = await db.Plans.IgnoreQueryFilters().Where(p => !p.IsDeleted).ToListAsync(ct);
        if (existing.Count == 0)
        {
            existing =
            [
                new Plan { Name = "Starter", Code = "starter", Description = "One school, core administration and academics.", PriceMonthly = 49, PriceYearly = 490, MaxSchools = 1, MaxUsers = 150, TrialDays = 30, SortOrder = 1 },
                new Plan { Name = "Standard", Code = "standard", Description = "Up to five schools with full academic management.", PriceMonthly = 149, PriceYearly = 1490, MaxSchools = 5, MaxUsers = 1000, TrialDays = 14, SortOrder = 2 },
                new Plan { Name = "Premium", Code = "premium", Description = "Unlimited schools and users for school groups.", PriceMonthly = 399, PriceYearly = 3990, TrialDays = 14, SortOrder = 3 }
            ];
            db.Plans.AddRange(existing);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Created default plans");
        }
        return existing.ToDictionary(p => p.Code);
    }

    /// <summary>Gives the demo tenants a realistic billing history: paid, partly paid, trial and overdue.</summary>
    private async Task CreateDemoBilling(Dictionary<string, Plan> plans, CancellationToken ct)
    {
        if (!plans.TryGetValue("starter", out var starter) || !plans.TryGetValue("standard", out var standard) || !plans.TryGetValue("premium", out var premium))
            return;

        var tenants = await db.Tenants.IgnoreQueryFilters().Where(t => !t.IsDeleted).ToDictionaryAsync(t => t.Code, ct);
        var now = DateTime.UtcNow;
        var seq = 0;
        string Number() => $"INV-{now.Year}-{++seq:D4}";

        if (tenants.TryGetValue("demo-academy", out var demo))
        {
            var start = now.Date.AddMonths(-1);
            var sub = AddSubscription(demo, standard, BillingCycle.Yearly, SubscriptionStatus.Active, start, start.AddYears(1), standard.PriceYearly, autoRenew: true);
            var paid = AddInvoice(demo, sub, Number(), start, 14, $"Standard plan, yearly subscription ({start:yyyy-MM-dd} to {start.AddYears(1):yyyy-MM-dd})", standard.PriceYearly);
            AddPayment(paid, paid.Total, start.AddDays(3), PaymentMethod.BankTransfer, "TRX-48211");

            var setup = AddInvoice(demo, sub, Number(), now.Date.AddDays(-10), 30, "Onboarding and data migration", 600);
            AddPayment(setup, 250, now.Date.AddDays(-4), PaymentMethod.Card, "CARD-9921");
        }

        if (tenants.TryGetValue("green-valley", out var green))
            AddSubscription(green, starter, BillingCycle.Monthly, SubscriptionStatus.Trial, now.Date.AddDays(-5), now.Date.AddDays(25), 0, autoRenew: false, isTrial: true);

        if (tenants.TryGetValue("riverside", out var river))
        {
            var start = now.Date.AddDays(-20);
            var sub = AddSubscription(river, premium, BillingCycle.Monthly, SubscriptionStatus.Suspended, start, start.AddMonths(1), premium.PriceMonthly, autoRenew: true, notes: "Suspended for non-payment");
            AddInvoice(river, sub, Number(), start, 14, $"Premium plan, monthly subscription ({start:yyyy-MM-dd} to {start.AddMonths(1):yyyy-MM-dd})", premium.PriceMonthly);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo billing data created");
    }

    private Subscription AddSubscription(Tenant t, Plan plan, BillingCycle cycle, SubscriptionStatus status, DateTime start, DateTime end,
        decimal price, bool autoRenew, bool isTrial = false, string? notes = null)
    {
        var sub = new Subscription
        {
            TenantId = t.Id, PlanId = plan.Id, Status = status, IsTrial = isTrial, BillingCycle = cycle,
            StartUtc = start, EndUtc = end, Price = price, Currency = plan.Currency, AutoRenew = autoRenew, Notes = notes
        };
        db.Subscriptions.Add(sub);
        t.PlanId = plan.Id;
        t.SubscriptionStatus = status;
        t.SubscriptionStartUtc = start;
        t.SubscriptionEndUtc = end;
        return sub;
    }

    private Invoice AddInvoice(Tenant t, Subscription sub, string number, DateTime issued, int termDays, string description, decimal amount)
    {
        var invoice = new Invoice
        {
            TenantId = t.Id, SubscriptionId = sub.Id, Number = number, Status = InvoiceStatus.Issued,
            IssueDateUtc = issued, DueDateUtc = issued.AddDays(termDays), Currency = sub.Currency,
            Lines = [new InvoiceLine { Description = description, Quantity = 1, UnitPrice = amount }]
        };
        invoice.RecalculateTotals();
        db.Invoices.Add(invoice);
        return invoice;
    }

    private void AddPayment(Invoice invoice, decimal amount, DateTime paidAt, PaymentMethod method, string reference)
    {
        db.Payments.Add(new Payment
        {
            TenantId = invoice.TenantId, InvoiceId = invoice.Id, Amount = amount, Currency = invoice.Currency,
            PaidAtUtc = paidAt, Method = method, Reference = reference
        });
        invoice.AmountPaid += amount;
        invoice.RefreshPaymentStatus();
    }

    private async Task<Dictionary<RoleType, Role>> EnsureSystemRoles(CancellationToken ct)
    {
        var existing = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions)
            .Where(r => r.IsSystem).ToListAsync(ct);

        foreach (var type in Enum.GetValues<RoleType>())
        {
            var role = existing.FirstOrDefault(r => r.Type == type);
            if (role is null)
            {
                role = new Role { Name = type.DisplayName(), Type = type, IsSystem = true, Description = $"Built-in {type.DisplayName()} profile" };
                role.Permissions.AddRange(DefaultRolePermissions.For(type).Select(p => new RolePermission { RoleId = role.Id, Permission = p }));
                db.Roles.Add(role);
                existing.Add(role);
            }
            else if (type == RoleType.SuperAdmin)
            {
                // SuperAdmin always holds every permission, including ones added in later versions.
                foreach (var p in DefaultRolePermissions.For(type).Where(p => role.Permissions.All(x => x.Permission != p)))
                    role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
            }
        }
        await db.SaveChangesAsync(ct);
        return existing.ToDictionary(r => r.Type);
    }

    private async Task EnsureSuperAdmin(Role role, CancellationToken ct)
    {
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.RoleId == role.Id && !u.IsDeleted, ct)) return;
        if (string.IsNullOrWhiteSpace(_s.SuperAdminPassword))
            throw new InvalidOperationException("Set Seed:SuperAdminPassword in configuration before the first start.");

        db.Users.Add(NewUser(null, role, _s.SuperAdminEmail, "Platform", "Owner", _s.SuperAdminPassword));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created SuperAdmin account {Email}", _s.SuperAdminEmail);
    }

    private async Task CreateDemoData(Dictionary<RoleType, Role> roles, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_s.DemoPassword))
        {
            logger.LogWarning("Seed:DemoPassword is empty; demo data skipped.");
            return;
        }

        var now = DateTime.UtcNow;
        var demo = new Tenant
        {
            Name = "Demo Academy", Code = "demo-academy", ContactEmail = "contact@demo-academy.local", Country = "Morocco",
            SubscriptionStatus = SubscriptionStatus.Active, SubscriptionStartUtc = now.AddMonths(-1), SubscriptionEndUtc = now.AddYears(1)
        };
        var central = new School { TenantId = demo.Id, Name = "Central Campus", Code = "CENTRAL", City = "Casablanca", PrincipalName = "Amina Benali", Email = "central@demo-academy.local" };
        var north = new School { TenantId = demo.Id, Name = "North Campus", Code = "NORTH", City = "Tangier", PrincipalName = "Youssef Alaoui", Email = "north@demo-academy.local" };

        var green = new Tenant
        {
            Name = "Green Valley Schools", Code = "green-valley", ContactEmail = "office@greenvalley.local", Country = "France", DefaultLanguage = "fr",
            SubscriptionStatus = SubscriptionStatus.Trial, SubscriptionStartUtc = now.AddDays(-5), SubscriptionEndUtc = now.AddDays(25)
        };
        var greenMain = new School { TenantId = green.Id, Name = "Green Valley Primary", Code = "GVP", City = "Lyon" };

        var suspended = new Tenant
        {
            Name = "Riverside Institute", Code = "riverside", ContactEmail = "admin@riverside.local", Country = "Egypt", DefaultLanguage = "ar",
            SubscriptionStatus = SubscriptionStatus.Suspended, SubscriptionStartUtc = now.AddMonths(-6), SubscriptionEndUtc = now.AddMonths(6)
        };
        var riverSchool = new School { TenantId = suspended.Id, Name = "Riverside Secondary", Code = "RSS", City = "Cairo" };

        db.Tenants.AddRange(demo, green, suspended);
        db.Schools.AddRange(central, north, greenMain, riverSchool);

        var names = new Dictionary<RoleType, (string First, string Last)>
        {
            [RoleType.TenantAdmin] = ("Sara", "Haddad"),
            [RoleType.SchoolAdmin] = ("Karim", "Mansour"),
            [RoleType.Teacher] = ("Leila", "Fassi"),
            [RoleType.Parent] = ("Omar", "Tazi"),
            [RoleType.Student] = ("Nour", "Tazi")
        };
        foreach (var (type, email) in DemoUsers)
        {
            var u = NewUser(demo.Id, roles[type], email, names[type].First, names[type].Last, _s.DemoPassword);
            if (type.IsSchoolScoped()) u.Schools.Add(new UserSchool { UserId = u.Id, SchoolId = central.Id });
            db.Users.Add(u);
        }

        db.Users.Add(NewUser(green.Id, roles[RoleType.TenantAdmin], "admin@greenvalley.local", "Claire", "Martin", _s.DemoPassword, "fr"));
        db.Users.Add(NewUser(suspended.Id, roles[RoleType.TenantAdmin], "admin@riverside.local", "Hany", "Saleh", _s.DemoPassword, "ar"));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo data created: 3 tenants, 4 schools, {Count} users", DemoUsers.Length + 2);
    }

    private AppUser NewUser(Guid? tenantId, Role role, string email, string first, string last, string password, string lang = "en") => new()
    {
        TenantId = tenantId,
        RoleId = role.Id,
        Email = email,
        NormalizedEmail = email.NormalizeKey(),
        FirstName = first,
        LastName = last,
        PreferredLanguage = lang,
        PasswordHash = passwords.Hash(password)
    };
}

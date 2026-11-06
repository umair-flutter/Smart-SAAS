using System.ComponentModel.DataAnnotations;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Shared.Contracts;

// ---------- Academic years ----------

public class AcademicYearDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public int ClassCount { get; set; }
    public int StudentCount { get; set; }
    public List<TermDto> Terms { get; set; } = [];
}

public class TermDto
{
    public Guid? Id { get; set; }
    [Required, StringLength(60)] public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public class AcademicYearEditDto
{
    [Required, StringLength(40)] public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; } = new(DateTime.Today.Year, 9, 1);
    public DateOnly EndDate { get; set; } = new(DateTime.Today.Year + 1, 6, 30);
    public bool IsCurrent { get; set; }
    public List<TermDto> Terms { get; set; } = [];
}

// ---------- Subjects and rooms ----------

public class SubjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public string Color { get; set; } = "#0ea5e9";
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int ClassCount { get; set; }
}

public class SubjectEditDto
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(20)] public string Code { get; set; } = string.Empty;
    [Range(0.1, 20)] public decimal Coefficient { get; set; } = 1;
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Use a color like #0ea5e9.")] public string Color { get; set; } = "#0ea5e9";
    [StringLength(300)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RoomDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Building { get; set; }
    public int Capacity { get; set; }
    public string? Type { get; set; }
}

public class RoomEditDto
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(20)] public string Code { get; set; } = string.Empty;
    [StringLength(100)] public string? Building { get; set; }
    [Range(0, 2000)] public int Capacity { get; set; } = 30;
    [StringLength(50)] public string? Type { get; set; } = "Classroom";
}

// ---------- People ----------

/// <summary>Shared fields for creating an optional login together with a teacher, student or parent.</summary>
public abstract class PersonEditDto
{
    [Required, StringLength(100)] public string FirstName { get; set; } = string.Empty;
    [Required, StringLength(100)] public string LastName { get; set; } = string.Empty;
    [EmailAddress, StringLength(200)] public string? Email { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Optional tenant-defined status badge.</summary>
    public Guid? StatusOptionId { get; set; }

    /// <summary>Create a sign-in account (requires Email and Password). Ignored when one already exists.</summary>
    public bool CreateLogin { get; set; }
    [StringLength(100, MinimumLength = 8)] public string? Password { get; set; }
}

public class TeacherDto
{
    public StatusBadgeDto? Status { get; set; }
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? EmployeeNo { get; set; }
    public string? Specialty { get; set; }
    public DateOnly? HireDate { get; set; }
    public bool IsActive { get; set; }
    public bool HasLogin { get; set; }
    public List<string> Subjects { get; set; } = [];
    public int ClassCount { get; set; }
}

public class TeacherEditDto : PersonEditDto
{
    [StringLength(30)] public string? EmployeeNo { get; set; }
    [StringLength(100)] public string? Specialty { get; set; }
    public DateOnly? HireDate { get; set; }
}

public class StudentDto
{
    public StatusBadgeDto? Status { get; set; }
    public Guid Id { get; set; }
    public string StudentNo { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public bool HasLogin { get; set; }
    public Guid? ClassId { get; set; }
    public string? ClassName { get; set; }
    public List<string> Guardians { get; set; } = [];
}

public class StudentEditDto : PersonEditDto
{
    /// <summary>Generated when empty.</summary>
    [StringLength(30)] public string? StudentNo { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public DateOnly? AdmissionDate { get; set; }

    /// <summary>Create only: enrol into this class of the active academic year.</summary>
    public Guid? ClassId { get; set; }
}

public class StudentDetailDto : StudentDto
{
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public DateOnly? AdmissionDate { get; set; }
    public List<StudentGuardianDto> GuardianLinks { get; set; } = [];
    public List<EnrollmentDto> Enrollments { get; set; } = [];
}

public class StudentGuardianDto
{
    public Guid GuardianId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Relationship { get; set; } = "Parent";
    public bool IsPrimary { get; set; }
}

public class LinkGuardianDto
{
    [Required] public Guid? GuardianId { get; set; }
    [Required, StringLength(40)] public string Relationship { get; set; } = "Parent";
    public bool IsPrimary { get; set; }
}

public class EnrollmentDto
{
    public Guid Id { get; set; }
    public Guid AcademicYearId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;
    public Guid ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public EnrollmentStatus Status { get; set; }
    public DateOnly EnrolledOn { get; set; }
}

public class GuardianDto
{
    public StatusBadgeDto? Status { get; set; }
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Occupation { get; set; }
    public bool IsActive { get; set; }
    public bool HasLogin { get; set; }
    public List<string> Children { get; set; } = [];
}

public class GuardianEditDto : PersonEditDto
{
    [StringLength(100)] public string? Occupation { get; set; }
}

// ---------- Classes ----------

public class ClassDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Level { get; set; }
    public Guid AcademicYearId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int StudentCount { get; set; }
    public Guid? HomeroomTeacherId { get; set; }
    public string? HomeroomTeacherName { get; set; }
    public Guid? RoomId { get; set; }
    public string? RoomName { get; set; }
}

public class ClassEditDto
{
    [Required, StringLength(60)] public string Name { get; set; } = string.Empty;
    [StringLength(60)] public string? Level { get; set; }

    /// <summary>Defaults to the active academic year.</summary>
    public Guid? AcademicYearId { get; set; }

    [Range(1, 500)] public int Capacity { get; set; } = 30;
    public Guid? HomeroomTeacherId { get; set; }
    public Guid? RoomId { get; set; }
}

public class ClassDetailDto : ClassDto
{
    public List<ClassStudentDto> Students { get; set; } = [];
    public List<AssignmentDto> Assignments { get; set; } = [];
}

public class ClassStudentDto
{
    public Guid EnrollmentId { get; set; }
    public Guid StudentId { get; set; }
    public string StudentNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public EnrollmentStatus Status { get; set; }
    public DateOnly EnrolledOn { get; set; }
}

public class EnrollStudentsDto
{
    [MinLength(1)] public List<Guid> StudentIds { get; set; } = [];
}

public class EnrollmentStatusDto
{
    public EnrollmentStatus Status { get; set; }

    /// <summary>When moving a student, the class (same year) they move to.</summary>
    public Guid? NewClassId { get; set; }
}

public class AssignmentDto
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public Guid TeacherId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public decimal HoursPerWeek { get; set; }
}

public class AssignmentEditDto
{
    [Required] public Guid? SubjectId { get; set; }
    [Required] public Guid? TeacherId { get; set; }
    [Range(0, 60)] public decimal HoursPerWeek { get; set; } = 2;
}

// ---------- Timetable ----------

public class TimetableSlotDto
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public Guid SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string SubjectColor { get; set; } = "#0ea5e9";
    public Guid? TeacherId { get; set; }
    public string? TeacherName { get; set; }
    public Guid? RoomId { get; set; }
    public string? RoomName { get; set; }
}

public class TimetableSlotEditDto
{
    [Required] public Guid? ClassId { get; set; }
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
    public TimeOnly StartTime { get; set; } = new(8, 0);
    public TimeOnly EndTime { get; set; } = new(9, 0);
    [Required] public Guid? SubjectId { get; set; }
    public Guid? TeacherId { get; set; }
    public Guid? RoomId { get; set; }
}

/// <summary>Dropdown data for the active school and academic year, fetched in one call.</summary>
public class SchoolLookupsDto
{
    public Guid? AcademicYearId { get; set; }
    public List<LookupItem> Years { get; set; } = [];
    public List<LookupItem> Classes { get; set; } = [];
    public List<LookupItem> Subjects { get; set; } = [];
    public List<LookupItem> Teachers { get; set; } = [];
    public List<LookupItem> Rooms { get; set; } = [];
    public List<LookupItem> Guardians { get; set; } = [];
    public List<LookupItem> Terms { get; set; } = [];
    public Guid? CurrentTermId { get; set; }
}

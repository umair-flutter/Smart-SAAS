using SmartSchool.Domain.Common;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Domain.Entities;

public class AcademicYear : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>The year used by default when no year is picked in the top bar. One per school.</summary>
    public bool IsCurrent { get; set; }

    public List<Term> Terms { get; set; } = [];
}

public class Term : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid AcademicYearId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int SortOrder { get; set; }
}

public class Subject : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    /// <summary>Weight of the subject in report-card averages.</summary>
    public decimal Coefficient { get; set; } = 1;

    public string Color { get; set; } = "#0ea5e9";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Room : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Building { get; set; }
    public int Capacity { get; set; }
    public string? Type { get; set; }
}

public abstract class Person : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }

    /// <summary>Optional login account linked to this record.</summary>
    public Guid? UserId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Optional tenant-defined status badge (see <see cref="StatusOption"/>).</summary>
    public Guid? StatusOptionId { get; set; }
    public StatusOption? StatusOption { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public class Teacher : Person
{
    public string? EmployeeNo { get; set; }
    public string? Specialty { get; set; }
    public DateOnly? HireDate { get; set; }
}

public class Student : Person
{
    public string StudentNo { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public DateOnly? AdmissionDate { get; set; }

    public List<StudentGuardian> Guardians { get; set; } = [];
    public List<Enrollment> Enrollments { get; set; } = [];
}

public class Guardian : Person
{
    public string? Occupation { get; set; }
    public List<StudentGuardian> Students { get; set; } = [];
}

public class StudentGuardian
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid GuardianId { get; set; }
    public Guardian? Guardian { get; set; }
    public string Relationship { get; set; } = "Parent";
    public bool IsPrimary { get; set; }
}

public class SchoolClass : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid AcademicYearId { get; set; }
    public AcademicYear? AcademicYear { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Level { get; set; }
    public int Capacity { get; set; } = 30;
    public Guid? HomeroomTeacherId { get; set; }
    public Teacher? HomeroomTeacher { get; set; }
    public Guid? RoomId { get; set; }
    public Room? Room { get; set; }

    public List<Enrollment> Enrollments { get; set; } = [];
    public List<TeachingAssignment> Assignments { get; set; } = [];
}

/// <summary>A student's place in a class for one academic year (one row per student per year).</summary>
public class Enrollment : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid ClassId { get; set; }
    public SchoolClass? Class { get; set; }
    public Guid AcademicYearId { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;
    public DateOnly EnrolledOn { get; set; }
}

/// <summary>Which teacher teaches which subject to which class.</summary>
public class TeachingAssignment : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid ClassId { get; set; }
    public SchoolClass? Class { get; set; }
    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public Guid TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public decimal HoursPerWeek { get; set; }
}

public class TimetableSlot : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid ClassId { get; set; }
    public SchoolClass? Class { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public Guid? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public Guid? RoomId { get; set; }
    public Room? Room { get; set; }
}

using SmartSchool.Domain.Common;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Domain.Entities;

/// <summary>An uploaded document (absence justification, homework file, exam paper…).</summary>
public class StoredFile : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }

    /// <summary>Relative path inside the storage root.</summary>
    public string StoragePath { get; set; } = string.Empty;
}

/// <summary>A configurable reason for absence or lateness (e.g. Illness, Family, Unjustified).</summary>
public class AbsenceReason : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Absences with an excused reason do not count as unjustified.</summary>
    public bool IsExcused { get; set; }

    public bool RequiresDocument { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Daily attendance of one student in one class.</summary>
public class AttendanceRecord : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid ClassId { get; set; }
    public SchoolClass? Class { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public DateOnly Date { get; set; }
    public AttendanceStatus Status { get; set; }
    public TimeOnly? ArrivalTime { get; set; }
    public Guid? ReasonId { get; set; }
    public AbsenceReason? Reason { get; set; }
    public string? Note { get; set; }
    public Guid? DocumentId { get; set; }
    public StoredFile? Document { get; set; }
    public bool IsValidated { get; set; }
    public Guid? ValidatedBy { get; set; }
    public DateTime? ValidatedAtUtc { get; set; }
}

public class Homework : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid ClassId { get; set; }
    public SchoolClass? Class { get; set; }
    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public Guid? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public DateTime AssignedAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public decimal? MaxScore { get; set; }
    public bool AllowLateSubmission { get; set; } = true;
    public bool IsPublished { get; set; }
    public Guid? AttachmentId { get; set; }
    public StoredFile? Attachment { get; set; }

    public List<HomeworkSubmission> Submissions { get; set; } = [];
}

public class HomeworkSubmission : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid HomeworkId { get; set; }
    public Homework? Homework { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public string? Text { get; set; }
    public Guid? AttachmentId { get; set; }
    public StoredFile? Attachment { get; set; }
    public bool IsLate { get; set; }
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Submitted;
    public decimal? Score { get; set; }
    public string? Feedback { get; set; }
    public DateTime? GradedAtUtc { get; set; }
    public Guid? GradedBy { get; set; }
}

public class Exam : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid ClassId { get; set; }
    public SchoolClass? Class { get; set; }
    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public Guid TermId { get; set; }
    public Term? Term { get; set; }
    public string Title { get; set; } = string.Empty;
    public ExamType Type { get; set; }
    public DateOnly Date { get; set; }
    public decimal MaxScore { get; set; } = 20;

    /// <summary>Weight of this exam inside the subject's term average.</summary>
    public decimal Weight { get; set; } = 1;

    public string? Description { get; set; }
    public Guid? DocumentId { get; set; }
    public StoredFile? Document { get; set; }

    /// <summary>Results become visible to students and parents once published.</summary>
    public bool ResultsPublished { get; set; }

    public List<ExamResult> Results { get; set; } = [];
}

public class ExamResult : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid ExamId { get; set; }
    public Exam? Exam { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public decimal? Score { get; set; }
    public bool IsAbsent { get; set; }
    public string? Comment { get; set; }
}

/// <summary>Comments and publication state of one student's report card for one term.</summary>
public class ReportCard : BaseEntity, ISchoolOwned
{
    public Guid TenantId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid ClassId { get; set; }
    public Guid TermId { get; set; }
    public string? GeneralComment { get; set; }
    public ReportCardStatus Status { get; set; } = ReportCardStatus.Draft;
    public DateTime? PublishedAtUtc { get; set; }

    public List<ReportCardComment> SubjectComments { get; set; } = [];
}

public class ReportCardComment
{
    public Guid ReportCardId { get; set; }
    public Guid SubjectId { get; set; }
    public string Comment { get; set; } = string.Empty;
    public Guid? TeacherId { get; set; }
}

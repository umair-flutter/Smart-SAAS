using System.ComponentModel.DataAnnotations;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Shared.Contracts;

public class FileDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
}

// ---------- Attendance ----------

public class AbsenceReasonDto
{
    public Guid Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = string.Empty;
    public bool IsExcused { get; set; }
    public bool RequiresDocument { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AttendanceSheetDto
{
    public Guid ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public bool IsRecorded { get; set; }
    public bool IsValidated { get; set; }
    public List<AttendanceRowDto> Rows { get; set; } = [];
}

public class AttendanceRowDto
{
    public Guid StudentId { get; set; }
    public string StudentNo { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public Guid? RecordId { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
    public TimeOnly? ArrivalTime { get; set; }
    public Guid? ReasonId { get; set; }
    [StringLength(300)] public string? Note { get; set; }
    public FileDto? Document { get; set; }
}

public class AttendanceSaveDto
{
    [Required] public Guid? ClassId { get; set; }
    public DateOnly Date { get; set; }
    public List<AttendanceRowDto> Rows { get; set; } = [];
}

public class AttendanceRecordDto
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public Guid StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public AttendanceStatus Status { get; set; }
    public TimeOnly? ArrivalTime { get; set; }
    public string? ReasonName { get; set; }
    public bool IsExcused { get; set; }
    public string? Note { get; set; }
    public FileDto? Document { get; set; }
    public bool IsValidated { get; set; }
}

public class AttendanceSummaryDto
{
    public Guid StudentId { get; set; }
    public string StudentNo { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public int DaysRecorded { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int Excused { get; set; }
    public int Unjustified { get; set; }
    public decimal AttendanceRate { get; set; }
}

public class AttendanceJustifyDto
{
    public Guid? ReasonId { get; set; }
    [StringLength(300)] public string? Note { get; set; }
}

// ---------- Homework ----------

public class HomeworkDto
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string SubjectColor { get; set; } = "#0ea5e9";
    public string? TeacherName { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime AssignedAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public decimal? MaxScore { get; set; }
    public bool AllowLateSubmission { get; set; }
    public bool IsPublished { get; set; }
    public int StudentCount { get; set; }
    public int SubmittedCount { get; set; }
    public int LateCount { get; set; }
    public int GradedCount { get; set; }

    /// <summary>For a student or parent: the student's own submission state.</summary>
    public SubmissionStatus? MyStatus { get; set; }
    public bool? MyIsLate { get; set; }
    public decimal? MyScore { get; set; }
}

public class HomeworkDetailDto : HomeworkDto
{
    public string? Instructions { get; set; }
    public FileDto? Attachment { get; set; }
    public List<SubmissionDto> Submissions { get; set; } = [];

    /// <summary>Students of the class who have not submitted yet (staff view only).</summary>
    public List<LookupItem> Missing { get; set; } = [];
}

public class HomeworkEditDto
{
    [Required] public Guid? ClassId { get; set; }
    [Required] public Guid? SubjectId { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    [StringLength(4000)] public string? Instructions { get; set; }
    public DateTime DueAtUtc { get; set; } = DateTime.UtcNow.Date.AddDays(7).AddHours(17);
    [Range(0, 1000)] public decimal? MaxScore { get; set; } = 20;
    public bool AllowLateSubmission { get; set; } = true;
    public bool IsPublished { get; set; } = true;
}

public class SubmissionDto
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; }
    public string? Text { get; set; }
    public FileDto? Attachment { get; set; }
    public bool IsLate { get; set; }
    public SubmissionStatus Status { get; set; }
    public decimal? Score { get; set; }
    public string? Feedback { get; set; }
    public DateTime? GradedAtUtc { get; set; }
}

public class GradeSubmissionDto
{
    [Range(0, 1000)] public decimal? Score { get; set; }
    [StringLength(2000)] public string? Feedback { get; set; }
}

// ---------- Exams ----------

public class ExamDto
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string SubjectColor { get; set; } = "#0ea5e9";
    public Guid TermId { get; set; }
    public string TermName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ExamType Type { get; set; }
    public DateOnly Date { get; set; }
    public decimal MaxScore { get; set; }
    public decimal Weight { get; set; }
    public bool ResultsPublished { get; set; }
    public int ResultCount { get; set; }
    public decimal? Average { get; set; }

    /// <summary>For a student or parent: the student's own result.</summary>
    public decimal? MyScore { get; set; }
    public bool? MyAbsent { get; set; }
}

public class ExamDetailDto : ExamDto
{
    public string? Description { get; set; }
    public FileDto? Document { get; set; }
    public decimal? Highest { get; set; }
    public decimal? Lowest { get; set; }
    public List<ExamResultDto> Results { get; set; } = [];
}

public class ExamResultDto
{
    public Guid StudentId { get; set; }
    public string StudentNo { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public decimal? Score { get; set; }
    public bool IsAbsent { get; set; }
    [StringLength(300)] public string? Comment { get; set; }
}

public class ExamEditDto
{
    [Required] public Guid? ClassId { get; set; }
    [Required] public Guid? SubjectId { get; set; }

    /// <summary>Defaults to the term containing the exam date.</summary>
    public Guid? TermId { get; set; }

    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    public ExamType Type { get; set; } = ExamType.Test;
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Range(1, 1000)] public decimal MaxScore { get; set; } = 20;
    [Range(0.1, 10)] public decimal Weight { get; set; } = 1;
    [StringLength(1000)] public string? Description { get; set; }
}

public class ExamResultsSaveDto
{
    public List<ExamResultDto> Results { get; set; } = [];
}

// ---------- Report cards ----------

public class ReportCardSummaryDto
{
    public Guid StudentId { get; set; }
    public string StudentNo { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public decimal? Average { get; set; }
    public int? Rank { get; set; }
    public int Absences { get; set; }
    public ReportCardStatus Status { get; set; }
    public bool HasGeneralComment { get; set; }
}

public class ReportCardDto
{
    public Guid StudentId { get; set; }
    public string StudentNo { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public Guid ClassId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;
    public Guid TermId { get; set; }
    public string TermName { get; set; } = string.Empty;
    public string? HomeroomTeacher { get; set; }
    public List<ReportCardSubjectDto> Subjects { get; set; } = [];
    public decimal? Average { get; set; }
    public int? Rank { get; set; }
    public int ClassSize { get; set; }
    public decimal? ClassAverage { get; set; }
    public int AbsenceDays { get; set; }
    public int UnjustifiedAbsences { get; set; }
    public int LateCount { get; set; }
    public string? GeneralComment { get; set; }
    public ReportCardStatus Status { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>What the caller may change (computed per user).</summary>
    public bool CanEditGeneral { get; set; }
    public List<Guid> EditableSubjectIds { get; set; } = [];
}

public class ReportCardSubjectDto
{
    public Guid SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public string? TeacherName { get; set; }
    public decimal? Average { get; set; }
    public decimal? ClassAverage { get; set; }
    public int ExamCount { get; set; }
    public string? Comment { get; set; }
}

public class ReportCardCommentsDto
{
    [StringLength(2000)] public string? GeneralComment { get; set; }
    public List<SubjectCommentDto> Subjects { get; set; } = [];
}

public class SubjectCommentDto
{
    public Guid SubjectId { get; set; }
    [StringLength(1000)] public string? Comment { get; set; }
}

/// <summary>A child (for parents) or self (for students) shown in pickers.</summary>
public class MyStudentDto
{
    public Guid StudentId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public Guid? ClassId { get; set; }
    public string? ClassName { get; set; }
}

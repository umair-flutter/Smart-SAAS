namespace SmartSchool.Shared.Enums;

public enum AttendanceStatus
{
    Present = 1,
    Absent = 2,
    Late = 3,
    Excused = 4
}

public enum SubmissionStatus
{
    Submitted = 1,
    Graded = 2,
    Returned = 3
}

public enum ExamType
{
    Quiz = 1,
    Test = 2,
    Midterm = 3,
    Final = 4,
    Oral = 5,
    Project = 6
}

public enum ReportCardStatus
{
    Draft = 1,
    Published = 2
}

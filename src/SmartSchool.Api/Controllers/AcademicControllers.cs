using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.Api.Security;
using SmartSchool.Application.Common;
using SmartSchool.Application.Services.Academics;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Security;

namespace SmartSchool.Api.Controllers;

internal static class Uploads
{
    public static (Stream Content, string Name, string? Type, long Length)? From(IFormFile? file) =>
        file is null || file.Length == 0 ? null : (file.OpenReadStream(), file.FileName, file.ContentType, file.Length);
}

[ApiController]
[Authorize]
[Route("api")]
public class MeController(AccessScope scope, FileService files) : ControllerBase
{
    /// <summary>Children of a parent, or the student themself.</summary>
    [HttpGet("me/students")]
    public Task<List<MyStudentDto>> MyStudents(CancellationToken ct) => scope.MyStudentsAsync(ct);

    /// <summary>Downloads a document if the caller can see the record it belongs to.</summary>
    [HttpGet("files/{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var (content, name, type) = await files.OpenAsync(id, ct);
        return File(content, type, name);
    }
}

[ApiController]
[Authorize]
[Route("api/attendance")]
public class AttendanceController(AttendanceService attendance) : ControllerBase
{
    [HasPermission(Permissions.Attendance.View)]
    [HttpGet("reasons")]
    public Task<List<AbsenceReasonDto>> Reasons(CancellationToken ct) => attendance.ListReasonsAsync(ct);

    [HasPermission(Permissions.Attendance.Validate)]
    [HttpPost("reasons")]
    public async Task<object> CreateReason(AbsenceReasonDto dto, CancellationToken ct) => new { id = await attendance.SaveReasonAsync(null, dto, ct) };

    [HasPermission(Permissions.Attendance.Validate)]
    [HttpPut("reasons/{id:guid}")]
    public async Task<IActionResult> UpdateReason(Guid id, AbsenceReasonDto dto, CancellationToken ct)
    {
        await attendance.SaveReasonAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Attendance.Record)]
    [HttpGet("sheet")]
    public Task<AttendanceSheetDto> Sheet([FromQuery] Guid classId, [FromQuery] DateOnly date, CancellationToken ct) =>
        attendance.GetSheetAsync(classId, date, ct);

    [HasPermission(Permissions.Attendance.Record)]
    [HttpPut("sheet")]
    public async Task<IActionResult> SaveSheet(AttendanceSaveDto dto, CancellationToken ct)
    {
        await attendance.SaveSheetAsync(dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Attendance.Validate)]
    [HttpPost("sheet/validate")]
    public async Task<IActionResult> Validate([FromQuery] Guid classId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        await attendance.ValidateAsync(classId, date, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Attendance.View)]
    [HttpGet("records")]
    public Task<PagedResult<AttendanceRecordDto>> Records([FromQuery] PagedQuery query, [FromQuery] Guid? classId, [FromQuery] Guid? studentId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] bool absencesOnly, CancellationToken ct) =>
        attendance.ListAsync(query, classId, studentId, from, to, absencesOnly, ct);

    [HasPermission(Permissions.Attendance.View)]
    [HttpGet("summary")]
    public Task<List<AttendanceSummaryDto>> Summary([FromQuery] Guid? classId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        attendance.SummaryAsync(classId, from, to, ct);

    /// <summary>Adds a reason, note and/or supporting document to an absence (multipart form).</summary>
    [HasPermission(Permissions.Attendance.View)]
    [HttpPost("records/{id:guid}/justify")]
    [RequestSizeLimit(FileService.MaxBytes + 1_000_000)]
    public async Task<IActionResult> Justify(Guid id, [FromForm] Guid? reasonId, [FromForm] string? note, IFormFile? file, CancellationToken ct)
    {
        await attendance.JustifyAsync(id, new AttendanceJustifyDto { ReasonId = reasonId, Note = note }, Uploads.From(file), ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/homework")]
public class HomeworkController(HomeworkService homework) : ControllerBase
{
    [HasPermission(Permissions.Homework.View)]
    [HttpGet]
    public Task<PagedResult<HomeworkDto>> List([FromQuery] PagedQuery query, [FromQuery] Guid? classId, [FromQuery] Guid? subjectId,
        [FromQuery] Guid? studentId, [FromQuery] bool upcoming, CancellationToken ct) =>
        homework.ListAsync(query, classId, subjectId, studentId, upcoming, ct);

    [HasPermission(Permissions.Homework.View)]
    [HttpGet("{id:guid}")]
    public Task<HomeworkDetailDto> Get(Guid id, [FromQuery] Guid? studentId, CancellationToken ct) => homework.GetAsync(id, studentId, ct);

    [HasPermission(Permissions.Homework.Manage)]
    [HttpPost]
    public async Task<object> Create(HomeworkEditDto dto, CancellationToken ct) => new { id = await homework.SaveAsync(null, dto, ct) };

    [HasPermission(Permissions.Homework.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, HomeworkEditDto dto, CancellationToken ct)
    {
        await homework.SaveAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Homework.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await homework.DeleteAsync(id, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Homework.Manage)]
    [HttpPost("{id:guid}/attachment")]
    [RequestSizeLimit(FileService.MaxBytes + 1_000_000)]
    public async Task<FileDto> Attach(Guid id, IFormFile file, CancellationToken ct) =>
        await homework.AttachAsync(id, file.OpenReadStream(), file.FileName, file.ContentType, file.Length, ct);

    [HasPermission(Permissions.Homework.Submit)]
    [HttpPost("{id:guid}/submit")]
    [RequestSizeLimit(FileService.MaxBytes + 1_000_000)]
    public async Task<IActionResult> Submit(Guid id, [FromForm] string? text, IFormFile? file, CancellationToken ct)
    {
        await homework.SubmitAsync(id, text, Uploads.From(file), ct);
        return NoContent();
    }

    [HasPermission(Permissions.Homework.Grade)]
    [HttpPost("submissions/{submissionId:guid}/grade")]
    public async Task<IActionResult> Grade(Guid submissionId, GradeSubmissionDto dto, CancellationToken ct)
    {
        await homework.GradeAsync(submissionId, dto, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/exams")]
public class ExamsController(ExamService exams) : ControllerBase
{
    [HasPermission(Permissions.Exams.View)]
    [HttpGet]
    public Task<List<ExamDto>> List([FromQuery] Guid? classId, [FromQuery] Guid? subjectId, [FromQuery] Guid? termId, [FromQuery] Guid? studentId, CancellationToken ct) =>
        exams.ListAsync(classId, subjectId, termId, studentId, ct);

    [HasPermission(Permissions.Exams.View)]
    [HttpGet("{id:guid}")]
    public Task<ExamDetailDto> Get(Guid id, [FromQuery] Guid? studentId, CancellationToken ct) => exams.GetAsync(id, studentId, ct);

    // Teachers create and grade exams for the subjects they teach (checked in the service).
    [HasPermission(Permissions.Exams.EnterResults)]
    [HttpPost]
    public async Task<object> Create(ExamEditDto dto, CancellationToken ct) => new { id = await exams.SaveAsync(null, dto, ct) };

    [HasPermission(Permissions.Exams.EnterResults)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ExamEditDto dto, CancellationToken ct)
    {
        await exams.SaveAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Exams.EnterResults)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await exams.DeleteAsync(id, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Exams.EnterResults)]
    [HttpPut("{id:guid}/results")]
    public async Task<IActionResult> SaveResults(Guid id, ExamResultsSaveDto dto, CancellationToken ct)
    {
        await exams.SaveResultsAsync(id, dto.Results, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Exams.EnterResults)]
    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, [FromQuery] bool publish, CancellationToken ct)
    {
        await exams.PublishAsync(id, publish, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Exams.EnterResults)]
    [HttpPost("{id:guid}/document")]
    [RequestSizeLimit(FileService.MaxBytes + 1_000_000)]
    public async Task<FileDto> Attach(Guid id, IFormFile file, CancellationToken ct) =>
        await exams.AttachAsync(id, file.OpenReadStream(), file.FileName, file.ContentType, file.Length, ct);
}

[ApiController]
[Authorize]
[Route("api/report-cards")]
public class ReportCardsController(ReportCardService cards) : ControllerBase
{
    [HasPermission(Permissions.ReportCards.View)]
    [HttpGet]
    public Task<List<ReportCardSummaryDto>> List([FromQuery] Guid classId, [FromQuery] Guid? termId, CancellationToken ct) =>
        cards.ListAsync(classId, termId, ct);

    [HasPermission(Permissions.ReportCards.View)]
    [HttpGet("student/{studentId:guid}")]
    public Task<ReportCardDto> Get(Guid studentId, [FromQuery] Guid? termId, CancellationToken ct) => cards.GetAsync(studentId, termId, ct);

    [HasPermission(Permissions.ReportCards.Manage)]
    [HttpPut("student/{studentId:guid}/comments")]
    public async Task<IActionResult> SaveComments(Guid studentId, [FromQuery] Guid termId, ReportCardCommentsDto dto, CancellationToken ct)
    {
        await cards.SaveCommentsAsync(studentId, termId, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.ReportCards.Manage)]
    [HttpPost("publish")]
    public async Task<object> Publish([FromQuery] Guid classId, [FromQuery] Guid? termId, [FromQuery] bool publish, CancellationToken ct) =>
        new { count = await cards.PublishAsync(classId, termId, publish, ct) };

    [HasPermission(Permissions.ReportCards.Export)]
    [HttpGet("student/{studentId:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid studentId, [FromQuery] Guid? termId, [FromQuery] string lang, CancellationToken ct)
    {
        var (content, name) = await cards.PdfAsync(studentId, termId, lang ?? "en", ct);
        return File(content, "application/pdf", name);
    }
}

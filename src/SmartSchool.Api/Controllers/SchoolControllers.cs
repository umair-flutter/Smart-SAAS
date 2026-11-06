using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.Api.Security;
using SmartSchool.Application.Services.Academics;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Security;

namespace SmartSchool.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/school")]
public class SchoolLookupsController(CatalogService catalog) : ControllerBase
{
    /// <summary>Dropdown data (years, classes, subjects, teachers, rooms, guardians) for the active school and year.</summary>
    [HttpGet("lookups")]
    public Task<SchoolLookupsDto> Lookups(CancellationToken ct) => catalog.LookupsAsync(ct);
}

[ApiController]
[Authorize]
[Route("api/academic-years")]
public class AcademicYearsController(AcademicYearService years) : ControllerBase
{
    [HasPermission(Permissions.AcademicYears.View)]
    [HttpGet]
    public Task<List<AcademicYearDto>> List(CancellationToken ct) => years.ListAsync(ct);

    [HasPermission(Permissions.AcademicYears.Manage)]
    [HttpPost]
    public async Task<object> Create(AcademicYearEditDto dto, CancellationToken ct) => new { id = await years.CreateAsync(dto, ct) };

    [HasPermission(Permissions.AcademicYears.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, AcademicYearEditDto dto, CancellationToken ct)
    {
        await years.UpdateAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.AcademicYears.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await years.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/subjects")]
public class SubjectsController(CatalogService catalog) : ControllerBase
{
    [HasPermission(Permissions.Classes.View)]
    [HttpGet]
    public Task<List<SubjectDto>> List(CancellationToken ct) => catalog.ListSubjectsAsync(ct);

    [HasPermission(Permissions.Classes.Manage)]
    [HttpPost]
    public async Task<object> Create(SubjectEditDto dto, CancellationToken ct) => new { id = await catalog.SaveSubjectAsync(null, dto, ct) };

    [HasPermission(Permissions.Classes.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SubjectEditDto dto, CancellationToken ct)
    {
        await catalog.SaveSubjectAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Classes.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await catalog.DeleteSubjectAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/rooms")]
public class RoomsController(CatalogService catalog) : ControllerBase
{
    [HasPermission(Permissions.Classes.View)]
    [HttpGet]
    public Task<List<RoomDto>> List(CancellationToken ct) => catalog.ListRoomsAsync(ct);

    [HasPermission(Permissions.Classes.Manage)]
    [HttpPost]
    public async Task<object> Create(RoomEditDto dto, CancellationToken ct) => new { id = await catalog.SaveRoomAsync(null, dto, ct) };

    [HasPermission(Permissions.Classes.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, RoomEditDto dto, CancellationToken ct)
    {
        await catalog.SaveRoomAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Classes.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await catalog.DeleteRoomAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/teachers")]
public class TeachersController(PeopleService people) : ControllerBase
{
    [HasPermission(Permissions.Teachers.View)]
    [HttpGet]
    public Task<PagedResult<TeacherDto>> List([FromQuery] PagedQuery query, CancellationToken ct) => people.ListTeachersAsync(query, ct);

    [HasPermission(Permissions.Teachers.View)]
    [HttpGet("{id:guid}")]
    public Task<TeacherEditDto> Get(Guid id, CancellationToken ct) => people.GetTeacherAsync(id, ct);

    [HasPermission(Permissions.Teachers.Manage)]
    [HttpPost]
    public async Task<object> Create(TeacherEditDto dto, CancellationToken ct) => new { id = await people.SaveTeacherAsync(null, dto, ct) };

    [HasPermission(Permissions.Teachers.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, TeacherEditDto dto, CancellationToken ct)
    {
        await people.SaveTeacherAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Teachers.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await people.DeleteTeacherAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/students")]
public class StudentsController(PeopleService people) : ControllerBase
{
    [HasPermission(Permissions.Students.View)]
    [HttpGet]
    public Task<PagedResult<StudentDto>> List([FromQuery] PagedQuery query, [FromQuery] Guid? classId, CancellationToken ct) =>
        people.ListStudentsAsync(query, classId, ct);

    [HasPermission(Permissions.Students.View)]
    [HttpGet("{id:guid}")]
    public Task<StudentDetailDto> Get(Guid id, CancellationToken ct) => people.GetStudentAsync(id, ct);

    [HasPermission(Permissions.Students.Edit)]
    [HttpGet("{id:guid}/edit")]
    public Task<StudentEditDto> GetForEdit(Guid id, CancellationToken ct) => people.GetStudentForEditAsync(id, ct);

    [HasPermission(Permissions.Students.Create)]
    [HttpPost]
    public async Task<object> Create(StudentEditDto dto, CancellationToken ct) => new { id = await people.SaveStudentAsync(null, dto, ct) };

    [HasPermission(Permissions.Students.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, StudentEditDto dto, CancellationToken ct)
    {
        await people.SaveStudentAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Students.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await people.DeleteStudentAsync(id, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Students.Edit)]
    [HttpPost("{id:guid}/guardians")]
    public async Task<IActionResult> LinkGuardian(Guid id, LinkGuardianDto dto, CancellationToken ct)
    {
        await people.LinkGuardianAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Students.Edit)]
    [HttpDelete("{id:guid}/guardians/{guardianId:guid}")]
    public async Task<IActionResult> UnlinkGuardian(Guid id, Guid guardianId, CancellationToken ct)
    {
        await people.UnlinkGuardianAsync(id, guardianId, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/guardians")]
public class GuardiansController(PeopleService people) : ControllerBase
{
    [HasPermission(Permissions.Parents.View)]
    [HttpGet]
    public Task<PagedResult<GuardianDto>> List([FromQuery] PagedQuery query, CancellationToken ct) => people.ListGuardiansAsync(query, ct);

    [HasPermission(Permissions.Parents.View)]
    [HttpGet("{id:guid}")]
    public Task<GuardianEditDto> Get(Guid id, CancellationToken ct) => people.GetGuardianAsync(id, ct);

    [HasPermission(Permissions.Parents.Manage)]
    [HttpPost]
    public async Task<object> Create(GuardianEditDto dto, CancellationToken ct) => new { id = await people.SaveGuardianAsync(null, dto, ct) };

    [HasPermission(Permissions.Parents.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, GuardianEditDto dto, CancellationToken ct)
    {
        await people.SaveGuardianAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Parents.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await people.DeleteGuardianAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/classes")]
public class ClassesController(ClassService classes) : ControllerBase
{
    [HasPermission(Permissions.Classes.View)]
    [HttpGet]
    public Task<List<ClassDto>> List(CancellationToken ct) => classes.ListAsync(ct);

    [HasPermission(Permissions.Classes.View)]
    [HttpGet("{id:guid}")]
    public Task<ClassDetailDto> Get(Guid id, CancellationToken ct) => classes.GetAsync(id, ct);

    [HasPermission(Permissions.Classes.Manage)]
    [HttpPost]
    public async Task<object> Create(ClassEditDto dto, CancellationToken ct) => new { id = await classes.SaveAsync(null, dto, ct) };

    [HasPermission(Permissions.Classes.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ClassEditDto dto, CancellationToken ct)
    {
        await classes.SaveAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Classes.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await classes.DeleteAsync(id, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Students.Edit)]
    [HttpPost("{id:guid}/enrollments")]
    public async Task<object> Enroll(Guid id, EnrollStudentsDto dto, CancellationToken ct) => new { added = await classes.EnrollAsync(id, dto.StudentIds, ct) };

    [HasPermission(Permissions.Students.Edit)]
    [HttpPut("enrollments/{enrollmentId:guid}")]
    public async Task<IActionResult> UpdateEnrollment(Guid enrollmentId, EnrollmentStatusDto dto, CancellationToken ct)
    {
        await classes.UpdateEnrollmentAsync(enrollmentId, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Students.Edit)]
    [HttpDelete("enrollments/{enrollmentId:guid}")]
    public async Task<IActionResult> RemoveEnrollment(Guid enrollmentId, CancellationToken ct)
    {
        await classes.RemoveEnrollmentAsync(enrollmentId, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Teachers.Manage)]
    [HttpPost("{id:guid}/assignments")]
    public async Task<object> AddAssignment(Guid id, AssignmentEditDto dto, CancellationToken ct) => new { id = await classes.SaveAssignmentAsync(id, null, dto, ct) };

    [HasPermission(Permissions.Teachers.Manage)]
    [HttpPut("{id:guid}/assignments/{assignmentId:guid}")]
    public async Task<IActionResult> UpdateAssignment(Guid id, Guid assignmentId, AssignmentEditDto dto, CancellationToken ct)
    {
        await classes.SaveAssignmentAsync(id, assignmentId, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Teachers.Manage)]
    [HttpDelete("{id:guid}/assignments/{assignmentId:guid}")]
    public async Task<IActionResult> DeleteAssignment(Guid id, Guid assignmentId, CancellationToken ct)
    {
        await classes.DeleteAssignmentAsync(id, assignmentId, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/timetable")]
public class TimetableController(TimetableService timetable) : ControllerBase
{
    [HasPermission(Permissions.Timetable.View)]
    [HttpGet]
    public Task<List<TimetableSlotDto>> Get([FromQuery] Guid? classId, [FromQuery] Guid? teacherId, [FromQuery] Guid? roomId, CancellationToken ct) =>
        timetable.GetAsync(classId, teacherId, roomId, ct);

    [HasPermission(Permissions.Timetable.Manage)]
    [HttpPost]
    public async Task<object> Create(TimetableSlotEditDto dto, CancellationToken ct) => new { id = await timetable.SaveAsync(null, dto, ct) };

    [HasPermission(Permissions.Timetable.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, TimetableSlotEditDto dto, CancellationToken ct)
    {
        await timetable.SaveAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Timetable.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await timetable.DeleteAsync(id, ct);
        return NoContent();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSchool.Domain.Entities;

namespace SmartSchool.Infrastructure.Persistence;

internal static class PersonConfig
{
    public static void Configure<T>(EntityTypeBuilder<T> b) where T : Person
    {
        b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Ignore(x => x.FullName);
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.StatusOption).WithMany().HasForeignKey(x => x.StatusOptionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SchoolId, x.LastName, x.FirstName });
        b.HasIndex(x => x.UserId);
    }
}

internal sealed class AcademicYearConfig : IEntityTypeConfiguration<AcademicYear>
{
    public void Configure(EntityTypeBuilder<AcademicYear> b)
    {
        b.Property(x => x.Name).HasMaxLength(40).IsRequired();
        b.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Terms).WithOne().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SchoolId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class TermConfig : IEntityTypeConfiguration<Term>
{
    public void Configure(EntityTypeBuilder<Term> b) => b.Property(x => x.Name).HasMaxLength(60).IsRequired();
}

internal sealed class SubjectConfig : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> b)
    {
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Color).HasMaxLength(7).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Coefficient).HasPrecision(5, 2);
        b.HasIndex(x => new { x.SchoolId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class RoomConfig : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> b)
    {
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Building).HasMaxLength(100);
        b.Property(x => x.Type).HasMaxLength(50);
        b.HasIndex(x => new { x.SchoolId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class TeacherConfig : IEntityTypeConfiguration<Teacher>
{
    public void Configure(EntityTypeBuilder<Teacher> b)
    {
        PersonConfig.Configure(b);
        b.Property(x => x.EmployeeNo).HasMaxLength(30);
        b.Property(x => x.Specialty).HasMaxLength(100);
    }
}

internal sealed class StudentConfig : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> b)
    {
        PersonConfig.Configure(b);
        b.Property(x => x.StudentNo).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.StudentNo }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class GuardianConfig : IEntityTypeConfiguration<Guardian>
{
    public void Configure(EntityTypeBuilder<Guardian> b)
    {
        PersonConfig.Configure(b);
        b.Property(x => x.Occupation).HasMaxLength(100);
    }
}

internal sealed class StudentGuardianConfig : IEntityTypeConfiguration<StudentGuardian>
{
    public void Configure(EntityTypeBuilder<StudentGuardian> b)
    {
        b.HasKey(x => new { x.StudentId, x.GuardianId });
        b.Property(x => x.Relationship).HasMaxLength(40);
        b.HasOne(x => x.Student).WithMany(s => s.Guardians).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Guardian).WithMany(g => g.Students).HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SchoolClassConfig : IEntityTypeConfiguration<SchoolClass>
{
    public void Configure(EntityTypeBuilder<SchoolClass> b)
    {
        b.ToTable("Classes");
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Level).HasMaxLength(60);
        b.HasOne(x => x.AcademicYear).WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.HomeroomTeacher).WithMany().HasForeignKey(x => x.HomeroomTeacherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SchoolId, x.AcademicYearId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class EnrollmentConfig : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> b)
    {
        b.HasOne(x => x.Student).WithMany(s => s.Enrollments).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Class).WithMany(c => c.Enrollments).HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StudentId, x.AcademicYearId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class TeachingAssignmentConfig : IEntityTypeConfiguration<TeachingAssignment>
{
    public void Configure(EntityTypeBuilder<TeachingAssignment> b)
    {
        b.Property(x => x.HoursPerWeek).HasPrecision(5, 2);
        b.HasOne(x => x.Class).WithMany(c => c.Assignments).HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ClassId, x.SubjectId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class TimetableSlotConfig : IEntityTypeConfiguration<TimetableSlot>
{
    public void Configure(EntityTypeBuilder<TimetableSlot> b)
    {
        b.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SchoolId, x.DayOfWeek });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSchool.Domain.Entities;

namespace SmartSchool.Infrastructure.Persistence;

internal sealed class StoredFileConfig : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("Files");
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(400).IsRequired();
    }
}

internal sealed class AbsenceReasonConfig : IEntityTypeConfiguration<AbsenceReason>
{
    public void Configure(EntityTypeBuilder<AbsenceReason> b) => b.Property(x => x.Name).HasMaxLength(80).IsRequired();
}

internal sealed class AttendanceRecordConfig : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> b)
    {
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Reason).WithMany().HasForeignKey(x => x.ReasonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StudentId, x.Date }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.ClassId, x.Date });
    }
}

internal sealed class HomeworkConfig : IEntityTypeConfiguration<Homework>
{
    public void Configure(EntityTypeBuilder<Homework> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Instructions).HasMaxLength(4000);
        b.Property(x => x.MaxScore).HasPrecision(7, 2);
        b.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Attachment).WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Submissions).WithOne(s => s.Homework).HasForeignKey(s => s.HomeworkId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ClassId, x.DueAtUtc });
    }
}

internal sealed class HomeworkSubmissionConfig : IEntityTypeConfiguration<HomeworkSubmission>
{
    public void Configure(EntityTypeBuilder<HomeworkSubmission> b)
    {
        b.Property(x => x.Text).HasMaxLength(4000);
        b.Property(x => x.Feedback).HasMaxLength(2000);
        b.Property(x => x.Score).HasPrecision(7, 2);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Attachment).WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HomeworkId, x.StudentId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class ExamConfig : IEntityTypeConfiguration<Exam>
{
    public void Configure(EntityTypeBuilder<Exam> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.MaxScore).HasPrecision(7, 2);
        b.Property(x => x.Weight).HasPrecision(5, 2);
        b.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Term).WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Results).WithOne(r => r.Exam).HasForeignKey(r => r.ExamId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ClassId, x.TermId, x.SubjectId });
    }
}

internal sealed class ExamResultConfig : IEntityTypeConfiguration<ExamResult>
{
    public void Configure(EntityTypeBuilder<ExamResult> b)
    {
        b.Property(x => x.Score).HasPrecision(7, 2);
        b.Property(x => x.Comment).HasMaxLength(300);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ExamId, x.StudentId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class ReportCardConfig : IEntityTypeConfiguration<ReportCard>
{
    public void Configure(EntityTypeBuilder<ReportCard> b)
    {
        b.Property(x => x.GeneralComment).HasMaxLength(2000);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.SubjectComments).WithOne().HasForeignKey(x => x.ReportCardId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.StudentId, x.TermId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class ReportCardCommentConfig : IEntityTypeConfiguration<ReportCardComment>
{
    public void Configure(EntityTypeBuilder<ReportCardComment> b)
    {
        b.HasKey(x => new { x.ReportCardId, x.SubjectId });
        b.Property(x => x.Comment).HasMaxLength(1000);
    }
}

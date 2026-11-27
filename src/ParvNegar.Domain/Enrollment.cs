using System;

namespace ParvNegar.Domain
{
    public enum EnrollmentStatus
    {
        Active = 0,
        Completed = 1,   // finished grade 6
        Withdrawn = 2,
        Repeating = 3,   // enrolled again in the same grade
    }

    /// <summary>
    /// Year-specific snapshot of values that may change later. Printing a historical year uses
    /// the snapshot, not the current Student or Classroom values (SRS AY-04).
    /// </summary>
    public sealed class EnrollmentSnapshot
    {
        public EnrollmentSnapshot(string studentName, string classroomName, string teacherName, string schoolTitle)
        {
            StudentName = studentName;
            ClassroomName = classroomName;
            TeacherName = teacherName;
            SchoolTitle = schoolTitle;
        }

        public string StudentName { get; }
        public string ClassroomName { get; }
        public string TeacherName { get; }
        public string SchoolTitle { get; }
    }

    /// <summary>
    /// One student in one school in one academic year. Uniqueness on (StudentId, SchoolId,
    /// AcademicYearId) is enforced by the promotion planner and by a database constraint.
    /// </summary>
    public sealed class Enrollment
    {
        public Enrollment(Guid id, Guid studentId, Guid schoolId, Guid academicYearId, GradeLevel grade, Guid classroomId,
            EnrollmentStatus status, EnrollmentSnapshot snapshot)
        {
            Guard.NotEmpty(id, nameof(id));
            Guard.NotEmpty(studentId, nameof(studentId));
            Guard.NotEmpty(schoolId, nameof(schoolId));
            Guard.NotEmpty(academicYearId, nameof(academicYearId));
            Guard.NotEmpty(classroomId, nameof(classroomId));
            Id = id;
            StudentId = studentId;
            SchoolId = schoolId;
            AcademicYearId = academicYearId;
            Grade = grade;
            ClassroomId = classroomId;
            Status = status;
            Snapshot = snapshot ?? throw new DomainException("snapshot.required", "Enrollment snapshot is required.");
        }

        public Guid Id { get; }
        public Guid StudentId { get; }
        public Guid SchoolId { get; }
        public Guid AcademicYearId { get; }
        public GradeLevel Grade { get; }
        public Guid ClassroomId { get; private set; }
        public EnrollmentStatus Status { get; private set; }
        public EnrollmentSnapshot Snapshot { get; private set; }

        /// <summary>Transfer within the same year and grade. Prior academic records stay attached to this enrollment.</summary>
        public void TransferTo(Classroom target, AcademicYear year)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (year == null || year.Id != AcademicYearId) throw new DomainException("transfer.wrong_year", "Year does not match the enrollment.");
            year.EnsureWritable();
            if (target.AcademicYearId != AcademicYearId) throw new DomainException("transfer.wrong_year", "Target class belongs to another year.");
            if (target.Grade != Grade) throw new DomainException("transfer.grade_mismatch", "Target class grade differs from the enrollment grade.");
            ClassroomId = target.Id;
            Snapshot = new EnrollmentSnapshot(Snapshot.StudentName, target.Name, target.TeacherName, Snapshot.SchoolTitle);
        }

        public bool IsSamePlacement(Enrollment other) =>
            other != null && other.StudentId == StudentId && other.AcademicYearId == AcademicYearId &&
            other.Grade == Grade && other.ClassroomId == ClassroomId && other.Status == Status;
    }
}

using System;

namespace ParvNegar.Domain
{
    /// <summary>Year-specific class with grade and teacher (SRS AY-02).</summary>
    public sealed class Classroom
    {
        public Classroom(Guid id, Guid academicYearId, GradeLevel grade, string code, string name, string teacherName)
        {
            Guard.NotEmpty(id, nameof(id));
            Guard.NotEmpty(academicYearId, nameof(academicYearId));
            Id = id;
            AcademicYearId = academicYearId;
            Grade = grade;
            Code = Guard.RequiredText(code, nameof(code), 20);
            Name = Guard.RequiredText(name, nameof(name), 100);
            TeacherName = Guard.OptionalText(teacherName, nameof(teacherName), 150);
        }

        public Guid Id { get; }
        public Guid AcademicYearId { get; }
        public GradeLevel Grade { get; }
        public string Code { get; }
        public string Name { get; }
        public string TeacherName { get; }
    }
}

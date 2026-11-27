using System;

namespace ParvNegar.Domain
{
    /// <summary>
    /// Stable student identity. The ID is a random surrogate key and never derives from the
    /// national ID or the name (SRS STU-01). Annual data lives on Enrollment.
    /// </summary>
    public sealed class Student
    {
        public Student(Guid id, string fullName, string fatherName, NationalId nationalId, bool isTargetGroup)
        {
            Guard.NotEmpty(id, nameof(id));
            Id = id;
            FullName = Guard.RequiredText(fullName, nameof(fullName), 150);
            FatherName = Guard.OptionalText(fatherName, nameof(fatherName), 150);
            NationalId = nationalId;
            IsTargetGroup = isTargetGroup;
        }

        public Guid Id { get; }
        public string FullName { get; private set; }
        public string FatherName { get; private set; }

        /// <summary>Optional: unknown IDs are allowed until O-07 decides otherwise.</summary>
        public NationalId NationalId { get; private set; }

        /// <summary>Configurable target-group flag; no automated eligibility (SRS STU-01, O-07).</summary>
        public bool IsTargetGroup { get; private set; }

        public bool IsArchived { get; private set; }

        public void Rename(string fullName) => FullName = Guard.RequiredText(fullName, nameof(fullName), 150);
        public void SetNationalId(NationalId id) => NationalId = id;
        public void Archive() => IsArchived = true;
        public void Restore() => IsArchived = false;
    }
}

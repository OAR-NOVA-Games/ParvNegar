using System;

namespace ParvNegar.Domain
{
    public enum AcademicYearState
    {
        Draft = 0,
        Active = 1,
        Closed = 2,
    }

    /// <summary>
    /// Academic year (Mehr–Khordad). State transitions: Draft → Active → Closed.
    /// A closed year is read-only in normal workflows. Exceptional correction (reopen) is a
    /// proposed owner-only, audited operation pending O-08 and is NOT implemented here.
    /// </summary>
    public sealed class AcademicYear
    {
        public AcademicYear(Guid id, Guid schoolId, int startSolarYear, AcademicYearState state)
        {
            Guard.NotEmpty(id, nameof(id));
            Guard.NotEmpty(schoolId, nameof(schoolId));
            AcademicCalendar.StartDate(startSolarYear); // validates range
            Id = id;
            SchoolId = schoolId;
            StartSolarYear = startSolarYear;
            State = state;
        }

        public Guid Id { get; }
        public Guid SchoolId { get; }
        public int StartSolarYear { get; }
        public AcademicYearState State { get; private set; }

        public DateTime StartDate => AcademicCalendar.StartDate(StartSolarYear);
        public DateTime EndDate => AcademicCalendar.EndDate(StartSolarYear);
        public string Label => AcademicCalendar.Label(StartSolarYear);
        public bool IsWritable => State != AcademicYearState.Closed;

        public void Activate()
        {
            if (State != AcademicYearState.Draft) throw new DomainException("year.invalid_transition", "Only a draft year can be activated.");
            State = AcademicYearState.Active;
        }

        public void Close()
        {
            if (State != AcademicYearState.Active) throw new DomainException("year.invalid_transition", "Only an active year can be closed.");
            State = AcademicYearState.Closed;
        }

        public void EnsureWritable()
        {
            if (!IsWritable) throw new DomainException("year.closed", "The academic year is closed and read-only.");
        }
    }
}

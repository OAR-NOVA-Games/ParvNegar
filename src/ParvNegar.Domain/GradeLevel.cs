using System;

namespace ParvNegar.Domain
{
    /// <summary>Elementary grade 1–6 (SRS AY-02). Grade 6 is the final supported grade.</summary>
    public readonly struct GradeLevel : IEquatable<GradeLevel>
    {
        public const int Min = 1;
        public const int Max = 6;

        public GradeLevel(int value)
        {
            if (value < Min || value > Max) throw new DomainException("grade.out_of_range", "Grade must be between " + Min + " and " + Max + ".");
            Value = value;
        }

        public int Value { get; }
        public bool IsFinal => Value == Max;

        /// <summary>Next grade. Grade 6 has no next grade; promotion out of grade 6 is completion, never grade 7.</summary>
        public GradeLevel Next()
        {
            if (IsFinal) throw new DomainException("grade.no_next", "Grade 6 cannot be promoted to an unsupported grade.");
            return new GradeLevel(Value + 1);
        }

        public bool Equals(GradeLevel other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GradeLevel g && Equals(g);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static bool operator ==(GradeLevel a, GradeLevel b) => a.Equals(b);
        public static bool operator !=(GradeLevel a, GradeLevel b) => !a.Equals(b);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace ParvNegar.Domain
{
    /// <summary>Explicit per-student outcome chosen by an authorized user. There is no default outcome.</summary>
    public enum PromotionDecision
    {
        Promote = 0,   // grade + 1 (grades 1–5 only)
        Repeat = 1,    // same grade in the target year
        Complete = 2,  // grade 6 finished; no target enrollment
        Withdraw = 3,  // leaves the school; no target enrollment
    }

    public sealed class PromotionRequest
    {
        public PromotionRequest(Enrollment source, PromotionDecision decision, Guid? targetClassroomId)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Decision = decision;
            TargetClassroomId = targetClassroomId;
        }

        public Enrollment Source { get; }
        public PromotionDecision Decision { get; }
        public Guid? TargetClassroomId { get; }
    }

    public enum PromotionItemOutcome
    {
        Create = 0,          // a new target enrollment will be created
        AlreadyApplied = 1,  // identical target enrollment exists: idempotent no-op
        NoTarget = 2,        // complete / withdraw: no target enrollment
        Conflict = 3,        // blocked; see ConflictCode
    }

    public sealed class PromotionItem
    {
        internal PromotionItem(PromotionRequest request, PromotionItemOutcome outcome, Enrollment target, string conflictCode)
        {
            Request = request; Outcome = outcome; Target = target; ConflictCode = conflictCode;
        }

        public PromotionRequest Request { get; }
        public PromotionItemOutcome Outcome { get; }
        public Enrollment Target { get; }
        public string ConflictCode { get; }
    }

    public sealed class PromotionPlan
    {
        internal PromotionPlan(IReadOnlyList<PromotionItem> items) { Items = items; }
        public IReadOnlyList<PromotionItem> Items { get; }
        public bool HasConflicts => Items.Any(i => i.Outcome == PromotionItemOutcome.Conflict);
        public IEnumerable<Enrollment> EnrollmentsToCreate => Items.Where(i => i.Outcome == PromotionItemOutcome.Create).Select(i => i.Target);
    }

    /// <summary>
    /// Pure, side-effect-free promotion planning (SRS AY-03/AY-04). The plan is a preview with a
    /// conflict report. Applying it must happen in one database transaction; re-running the same
    /// requests yields AlreadyApplied items, so promotion is idempotent. Source enrollments are
    /// never modified. Copy rules beyond identity/snapshot are pending O-08.
    /// </summary>
    public static class PromotionPlanner
    {
        public const string ConflictTargetYearClosed = "target_year_closed";
        public const string ConflictSameYear = "target_year_equals_source";
        public const string ConflictSourceYearMismatch = "source_not_in_source_year";
        public const string ConflictFinalGrade = "grade6_cannot_promote";
        public const string ConflictCompleteNotFinal = "complete_requires_grade6";
        public const string ConflictMissingClass = "target_class_missing";
        public const string ConflictClassWrongYear = "target_class_other_year";
        public const string ConflictClassGradeMismatch = "target_class_grade_mismatch";
        public const string ConflictDuplicateEnrollment = "duplicate_target_enrollment";
        public const string ConflictDuplicateRequest = "duplicate_request";
        public const string ConflictUnexpectedClass = "class_given_for_no_target";

        public static PromotionPlan Plan(
            AcademicYear sourceYear,
            AcademicYear targetYear,
            IEnumerable<PromotionRequest> requests,
            IEnumerable<Classroom> targetClassrooms,
            IEnumerable<Enrollment> existingTargetEnrollments,
            Func<Guid> newId)
        {
            if (sourceYear == null) throw new ArgumentNullException(nameof(sourceYear));
            if (targetYear == null) throw new ArgumentNullException(nameof(targetYear));
            if (newId == null) throw new ArgumentNullException(nameof(newId));

            var classes = (targetClassrooms ?? Enumerable.Empty<Classroom>()).ToDictionary(c => c.Id);
            var existing = (existingTargetEnrollments ?? Enumerable.Empty<Enrollment>())
                .Where(e => e.AcademicYearId == targetYear.Id)
                .ToDictionary(e => e.StudentId);
            var seenStudents = new HashSet<Guid>();
            var items = new List<PromotionItem>();

            foreach (var r in requests ?? Enumerable.Empty<PromotionRequest>())
            {
                string conflict = null;
                Enrollment target = null;
                var src = r.Source;

                if (!seenStudents.Add(src.StudentId)) conflict = ConflictDuplicateRequest;
                else if (targetYear.Id == sourceYear.Id || targetYear.StartSolarYear <= sourceYear.StartSolarYear) conflict = ConflictSameYear;
                else if (src.AcademicYearId != sourceYear.Id) conflict = ConflictSourceYearMismatch;

                if (conflict == null && (r.Decision == PromotionDecision.Complete || r.Decision == PromotionDecision.Withdraw))
                {
                    if (r.Decision == PromotionDecision.Complete && !src.Grade.IsFinal) conflict = ConflictCompleteNotFinal;
                    else if (r.TargetClassroomId.HasValue) conflict = ConflictUnexpectedClass;
                    items.Add(new PromotionItem(r, conflict == null ? PromotionItemOutcome.NoTarget : PromotionItemOutcome.Conflict, null, conflict));
                    continue;
                }

                if (conflict == null && !targetYear.IsWritable) conflict = ConflictTargetYearClosed;

                GradeLevel targetGrade = src.Grade;
                if (conflict == null && r.Decision == PromotionDecision.Promote)
                {
                    if (src.Grade.IsFinal) conflict = ConflictFinalGrade;
                    else targetGrade = src.Grade.Next();
                }

                Classroom cls = null;
                if (conflict == null)
                {
                    if (!r.TargetClassroomId.HasValue || !classes.TryGetValue(r.TargetClassroomId.Value, out cls)) conflict = ConflictMissingClass;
                    else if (cls.AcademicYearId != targetYear.Id) conflict = ConflictClassWrongYear;
                    else if (cls.Grade != targetGrade) conflict = ConflictClassGradeMismatch;
                }

                if (conflict == null)
                {
                    var status = r.Decision == PromotionDecision.Repeat ? EnrollmentStatus.Repeating : EnrollmentStatus.Active;
                    var snapshot = new EnrollmentSnapshot(src.Snapshot.StudentName, cls.Name, cls.TeacherName, src.Snapshot.SchoolTitle);
                    var candidate = new Enrollment(newId(), src.StudentId, src.SchoolId, targetYear.Id, targetGrade, cls.Id, status, snapshot);

                    if (existing.TryGetValue(src.StudentId, out var already))
                    {
                        if (already.IsSamePlacement(candidate)) { items.Add(new PromotionItem(r, PromotionItemOutcome.AlreadyApplied, already, null)); continue; }
                        conflict = ConflictDuplicateEnrollment;
                    }
                    else target = candidate;
                }

                items.Add(conflict == null
                    ? new PromotionItem(r, PromotionItemOutcome.Create, target, null)
                    : new PromotionItem(r, PromotionItemOutcome.Conflict, null, conflict));
            }

            return new PromotionPlan(items);
        }
    }
}

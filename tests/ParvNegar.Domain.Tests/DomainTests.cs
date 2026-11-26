using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ParvNegar.Domain;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Domain.Tests
{
    public class CalendarAndIdentityTests
    {
        // Exact owner-approved title (SRS §1). Compared as UTF-16 code units; any normalization fails.
        private const string ApprovedTitle = "ثبت شاخص های آموزشی و پرورشی دانش آموزان جامعه هدف مدرسه شاهد شهیده شهناز کریمی";

        public CalendarAndIdentityTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Fact]
        public void Home_title_is_preserved_byte_for_byte()
        {
            var p = new SchoolProfile(Guid.NewGuid(), "مدرسه نمونه", ApprovedTitle, null);
            Assert.Equal(ApprovedTitle, p.HomeTitle);
            Assert.Equal(Encoding.UTF8.GetBytes(ApprovedTitle), Encoding.UTF8.GetBytes(p.HomeTitle));
            Assert.DoesNotContain('‌', p.HomeTitle); // source title uses plain spaces, no ZWNJ
            Assert.Equal("70078c64", Fnv1a(ApprovedTitle)); // FNV-1a of the UTF-8 title extracted from SRS §1 (computed independently)
            Assert.Equal(79, ApprovedTitle.Length);
        }

        [Fact]
        public void Title_with_surrounding_spaces_is_kept_as_configured()
        {
            var p = new SchoolProfile(Guid.NewGuid(), "x", " " + ApprovedTitle + " ", null);
            Assert.Equal(" " + ApprovedTitle + " ", p.HomeTitle);
        }

        [Theory]
        [InlineData(1404, "2025-09-23", "2026-06-21")]
        [InlineData(1405, "2026-09-23", "2027-06-21")]
        [InlineData(1403, "2024-09-22", "2025-06-21")]
        public void Academic_year_spans_1_mehr_to_end_of_khordad(int y, string start, string end)
        {
            Assert.Equal(DateTime.ParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture), AcademicCalendar.StartDate(y));
            Assert.Equal(DateTime.ParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture), AcademicCalendar.EndDate(y));
            var pc = new PersianCalendar();
            Assert.Equal(7, pc.GetMonth(AcademicCalendar.StartDate(y)));
            Assert.Equal(1, pc.GetDayOfMonth(AcademicCalendar.StartDate(y)));
            Assert.Equal(3, pc.GetMonth(AcademicCalendar.EndDate(y)));
            Assert.Equal(4, pc.GetMonth(AcademicCalendar.EndDate(y).AddDays(1))); // next day is 1 Tir
        }

        [Fact]
        public void Summer_gap_belongs_to_no_academic_year()
        {
            var pc = new PersianCalendar();
            Assert.Null(AcademicCalendar.AcademicStartYearOf(pc.ToDateTime(1405, 5, 10, 0, 0, 0, 0)));
            Assert.Equal(1404, AcademicCalendar.AcademicStartYearOf(pc.ToDateTime(1405, 2, 10, 0, 0, 0, 0)));
            Assert.Equal(1405, AcademicCalendar.AcademicStartYearOf(pc.ToDateTime(1405, 7, 1, 0, 0, 0, 0)));
            Assert.Equal("1404-1405", AcademicCalendar.Label(1404));
            Assert.Equal("۱۴۰۴-۱۴۰۵", PersianText.ToPersianDigits(AcademicCalendar.Label(1404)));
        }

        [Fact]
        public void Year_states_and_closure()
        {
            var y = new AcademicYear(Guid.NewGuid(), Guid.NewGuid(), 1405, AcademicYearState.Draft);
            Assert.Throws<DomainException>(() => y.Close());
            y.Activate();
            y.EnsureWritable();
            y.Close();
            Assert.False(y.IsWritable);
            Assert.Equal("year.closed", Assert.Throws<DomainException>(() => y.EnsureWritable()).Code);
            Assert.Throws<DomainException>(() => new AcademicYear(Guid.NewGuid(), Guid.NewGuid(), 1300, AcademicYearState.Draft));
        }

        [Fact]
        public void Grade_range_and_no_grade_seven()
        {
            Assert.Throws<DomainException>(() => new GradeLevel(0));
            Assert.Throws<DomainException>(() => new GradeLevel(7));
            Assert.Equal(2, new GradeLevel(1).Next().Value);
            Assert.Equal("grade.no_next", Assert.Throws<DomainException>(() => new GradeLevel(6).Next()).Code);
        }

        [Theory]
        [InlineData("۰۰۱۲۳۴۵۶۷۸", "0012345678")]
        [InlineData("٠٠١٢٣٤٥٦٧٨", "0012345678")]
        [InlineData("001-234567 8", "0012345678")]
        public void National_id_normalizes_digits_and_keeps_leading_zeros(string input, string digits)
        {
            Assert.Equal(digits, NationalId.Parse(input).Digits);
        }

        [Theory]
        [InlineData("123")]
        [InlineData("12345678901")]
        [InlineData("12345abcde")]
        [InlineData("")]
        [InlineData(null)]
        public void National_id_structure_is_validated(string input)
        {
            Assert.False(NationalId.TryParse(input, out _));
        }

        [Fact]
        public void National_id_is_never_printed_in_full()
        {
            var id = NationalId.Parse("0012345678");
            Assert.DoesNotContain("0012345678", id.ToString());
        }

        [Fact]
        public void Student_id_is_independent_of_national_id_and_name()
        {
            var id = Guid.NewGuid();
            var s = new Student(id, "دانش‌آموز نمونه", null, NationalId.Parse("0012345678"), false);
            s.Rename("نام جدید نمونه");
            s.SetNationalId(null);
            Assert.Equal(id, s.Id);
        }

        [Fact]
        public void Search_key_normalizes_arabic_yeh_kaf_digits_and_zwnj()
        {
            Assert.Equal(PersianText.SearchKey("مطالعات اجتماعی"), PersianText.SearchKey("مطالعات اجتماعي"));
            Assert.Equal(PersianText.SearchKey("کلاس"), PersianText.SearchKey("كلاس"));
            Assert.Equal("دانشآموز 12", PersianText.SearchKey("دانش‌آموز  ۱۲"));
            Assert.Equal("رياضيات".Length, "رياضيات".Length); // display text itself is not changed by SearchKey
        }

        private static string Fnv1a(string s)
        {
            uint h = 2166136261;
            foreach (var b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 16777619; }
            return h.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    public class PromotionTests
    {
        private readonly Guid _school = Guid.NewGuid();
        private readonly AcademicYear _y1, _y2;
        private readonly Dictionary<int, Classroom> _cls = new Dictionary<int, Classroom>();
        private const string Title = "عنوان آزمایشی";

        public PromotionTests()
        {
            TestEnvironment.AssertExpectedArchitecture();
            _y1 = new AcademicYear(Guid.NewGuid(), _school, 1404, AcademicYearState.Active);
            _y2 = new AcademicYear(Guid.NewGuid(), _school, 1405, AcademicYearState.Draft);
            for (int g = 1; g <= 6; g++) _cls[g] = new Classroom(Guid.NewGuid(), _y2.Id, new GradeLevel(g), "C" + g, "کلاس " + g, "آموزگار نمونه");
        }

        private Enrollment Src(int grade)
        {
            var srcClass = new Classroom(Guid.NewGuid(), _y1.Id, new GradeLevel(grade), "S" + grade, "مبدا " + grade, "آموزگار قبلی");
            return new Enrollment(Guid.NewGuid(), Guid.NewGuid(), _school, _y1.Id, new GradeLevel(grade), srcClass.Id, EnrollmentStatus.Active,
                new EnrollmentSnapshot("دانش‌آموز نمونه", srcClass.Name, srcClass.TeacherName, Title));
        }

        private PromotionPlan Plan(IEnumerable<PromotionRequest> reqs, IEnumerable<Enrollment> existing = null, AcademicYear target = null) =>
            PromotionPlanner.Plan(_y1, target ?? _y2, reqs, _cls.Values, existing, Guid.NewGuid);

        [Fact]
        public void Promote_creates_next_grade_enrollment_and_keeps_source_unchanged()
        {
            var s = Src(3);
            var before = (s.Id, s.Grade, s.ClassroomId, s.Status, s.Snapshot.ClassroomName);
            var plan = Plan(new[] { new PromotionRequest(s, PromotionDecision.Promote, _cls[4].Id) });
            var item = Assert.Single(plan.Items);
            Assert.Equal(PromotionItemOutcome.Create, item.Outcome);
            Assert.Equal(4, item.Target.Grade.Value);
            Assert.Equal(s.StudentId, item.Target.StudentId);
            Assert.Equal(_y2.Id, item.Target.AcademicYearId);
            Assert.Equal("کلاس 4", item.Target.Snapshot.ClassroomName);
            Assert.Equal(before, (s.Id, s.Grade, s.ClassroomId, s.Status, s.Snapshot.ClassroomName));
        }

        [Fact]
        public void Repeating_the_same_promotion_is_idempotent()
        {
            var s = Src(2);
            var req = new[] { new PromotionRequest(s, PromotionDecision.Promote, _cls[3].Id) };
            var first = Plan(req);
            var created = first.EnrollmentsToCreate.ToList();
            var second = Plan(req, created);
            Assert.Equal(PromotionItemOutcome.AlreadyApplied, Assert.Single(second.Items).Outcome);
            Assert.Empty(second.EnrollmentsToCreate);
        }

        [Fact]
        public void Grade_six_cannot_be_promoted_but_can_complete()
        {
            var s = Src(6);
            Assert.Equal(PromotionPlanner.ConflictFinalGrade, Assert.Single(Plan(new[] { new PromotionRequest(s, PromotionDecision.Promote, _cls[6].Id) }).Items).ConflictCode);
            Assert.Equal(PromotionItemOutcome.NoTarget, Assert.Single(Plan(new[] { new PromotionRequest(s, PromotionDecision.Complete, null) }).Items).Outcome);
            Assert.Equal(PromotionPlanner.ConflictCompleteNotFinal, Assert.Single(Plan(new[] { new PromotionRequest(Src(5), PromotionDecision.Complete, null) }).Items).ConflictCode);
        }

        [Fact]
        public void Repeat_keeps_grade_and_withdraw_creates_nothing()
        {
            var r = Assert.Single(Plan(new[] { new PromotionRequest(Src(2), PromotionDecision.Repeat, _cls[2].Id) }).Items);
            Assert.Equal(2, r.Target.Grade.Value);
            Assert.Equal(EnrollmentStatus.Repeating, r.Target.Status);
            Assert.Equal(PromotionItemOutcome.NoTarget, Assert.Single(Plan(new[] { new PromotionRequest(Src(2), PromotionDecision.Withdraw, null) }).Items).Outcome);
        }

        [Fact]
        public void Class_must_exist_in_target_year_with_matching_grade()
        {
            Assert.Equal(PromotionPlanner.ConflictMissingClass, Assert.Single(Plan(new[] { new PromotionRequest(Src(1), PromotionDecision.Promote, Guid.NewGuid()) }).Items).ConflictCode);
            Assert.Equal(PromotionPlanner.ConflictClassGradeMismatch, Assert.Single(Plan(new[] { new PromotionRequest(Src(1), PromotionDecision.Promote, _cls[3].Id) }).Items).ConflictCode);
        }

        [Fact]
        public void Closed_target_year_and_backwards_promotion_are_conflicts()
        {
            var closed = new AcademicYear(Guid.NewGuid(), _school, 1406, AcademicYearState.Active); closed.Close();
            Assert.Equal(PromotionPlanner.ConflictTargetYearClosed, Assert.Single(PromotionPlanner.Plan(_y1, closed, new[] { new PromotionRequest(Src(1), PromotionDecision.Promote, _cls[2].Id) }, _cls.Values, null, Guid.NewGuid).Items).ConflictCode);
            var older = new AcademicYear(Guid.NewGuid(), _school, 1403, AcademicYearState.Draft);
            Assert.Equal(PromotionPlanner.ConflictSameYear, Assert.Single(PromotionPlanner.Plan(_y1, older, new[] { new PromotionRequest(Src(1), PromotionDecision.Promote, _cls[2].Id) }, _cls.Values, null, Guid.NewGuid).Items).ConflictCode);
        }

        [Fact]
        public void Different_existing_enrollment_is_a_duplicate_conflict_and_duplicate_requests_are_refused()
        {
            var s = Src(1);
            var other = new Enrollment(Guid.NewGuid(), s.StudentId, _school, _y2.Id, new GradeLevel(1), _cls[1].Id, EnrollmentStatus.Repeating, new EnrollmentSnapshot("x", "y", null, Title));
            Assert.Equal(PromotionPlanner.ConflictDuplicateEnrollment, Assert.Single(Plan(new[] { new PromotionRequest(s, PromotionDecision.Promote, _cls[2].Id) }, new[] { other }).Items).ConflictCode);
            var plan = Plan(new[] { new PromotionRequest(s, PromotionDecision.Promote, _cls[2].Id), new PromotionRequest(s, PromotionDecision.Repeat, _cls[1].Id) });
            Assert.Equal(PromotionPlanner.ConflictDuplicateRequest, plan.Items[1].ConflictCode);
            Assert.True(plan.HasConflicts);
        }

        [Fact]
        public void Transfer_within_year_requires_same_grade_and_open_year()
        {
            var s = Src(3);
            var same = new Classroom(Guid.NewGuid(), _y1.Id, new GradeLevel(3), "T3", "سوم ب", "آموزگار دیگر");
            s.TransferTo(same, _y1);
            Assert.Equal(same.Id, s.ClassroomId);
            Assert.Equal("سوم ب", s.Snapshot.ClassroomName);
            var wrong = new Classroom(Guid.NewGuid(), _y1.Id, new GradeLevel(4), "T4", "چهارم", null);
            Assert.Throws<DomainException>(() => s.TransferTo(wrong, _y1));
            _y1.Close();
            Assert.Throws<DomainException>(() => s.TransferTo(same, _y1));
        }
    }
}

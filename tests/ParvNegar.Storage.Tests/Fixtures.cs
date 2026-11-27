using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ParvNegar.Tests.Shared
{
    /// <summary>
    /// Synthetic fixtures. Names are invented; national IDs are generated so that they FAIL the Iranian
    /// national-code check digit, which guarantees they cannot be a real person's code.
    /// </summary>
    public static class Fixtures
    {
        public const string SchoolTitle = "ثبت شاخص های آموزشی و پرورشی دانش آموزان جامعه هدف مدرسه شاهد شهیده شهناز کریمی";

        public sealed class Ids
        {
            public Guid School, Year, Class, Student, Enrollment;
        }

        public static string InvalidCheckDigitNationalId(int seed)
        {
            var digits = (Math.Abs(seed) % 1000000000).ToString("D9", CultureInfo.InvariantCulture);
            int s = 0;
            for (int i = 0; i < 9; i++) s += (digits[i] - '0') * (10 - i);
            int r = s % 11;
            int valid = r < 2 ? r : 11 - r;
            int wrong = (valid + 1) % 10; // any digit other than the valid one
            var id = digits + wrong.ToString(CultureInfo.InvariantCulture);
            if (IsValidCheckDigit(id)) throw new InvalidOperationException("fixture generator produced a valid code");
            return id;
        }

        public static bool IsValidCheckDigit(string id)
        {
            if (id == null || id.Length != 10) return false;
            int s = 0;
            for (int i = 0; i < 9; i++) s += (id[i] - '0') * (10 - i);
            int r = s % 11, c = id[9] - '0';
            return (r < 2 && c == r) || (r >= 2 && c == 11 - r);
        }

        public static void SeedMinimal(SqliteConnection c, out Ids ids)
        {
            ids = new Ids { School = Guid.NewGuid(), Year = Guid.NewGuid(), Class = Guid.NewGuid(), Student = Guid.NewGuid() };
            Exec(c, "INSERT INTO school_profile(id, school_name, home_title, created_utc, updated_utc) VALUES ($a, 'مدرسه نمونه', $t, 't', 't');", ids.School, SchoolTitle);
            Exec(c, "INSERT INTO academic_year(id, school_id, start_solar_year, state, start_date, end_date) VALUES ($a, $b, 1405, 1, '2026-09-23', '2027-06-21');", ids.Year, ids.School);
            Exec(c, "INSERT INTO classroom(id, academic_year_id, grade, code, name, teacher_name) VALUES ($a, $b, 1, '101', 'اول الف', 'آموزگار نمونه');", ids.Class, ids.Year);
            Exec(c, "INSERT INTO student(id, full_name, full_name_key, national_id, is_target_group, created_utc, updated_utc) VALUES ($a, 'دانش‌آموز نمونه یک', 'دانشآموز نمونه یک', $t, 1, 't', 't');", ids.Student, InvalidCheckDigitNationalId(1234567));
            ids.Enrollment = Guid.NewGuid();
            InsertEnrollment(c, ids, ids.Enrollment);
        }

        public static void InsertEnrollment(SqliteConnection c, Ids ids, Guid enrollmentId)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO enrollment(id, student_id, school_id, academic_year_id, grade, classroom_id, status, snapshot_student_name, snapshot_classroom, snapshot_teacher, snapshot_school_title, created_utc) " +
                                  "VALUES ($e, $s, $sc, $y, 1, $c, 0, 'دانش‌آموز نمونه یک', 'اول الف', 'آموزگار نمونه', $t, 't');";
                cmd.Parameters.AddWithValue("$e", enrollmentId.ToString());
                cmd.Parameters.AddWithValue("$s", ids.Student.ToString());
                cmd.Parameters.AddWithValue("$sc", ids.School.ToString());
                cmd.Parameters.AddWithValue("$y", ids.Year.ToString());
                cmd.Parameters.AddWithValue("$c", ids.Class.ToString());
                cmd.Parameters.AddWithValue("$t", SchoolTitle);
                cmd.ExecuteNonQuery();
            }
        }

        private static void Exec(SqliteConnection c, string sql, Guid a, object b)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$a", a.ToString());
                if (sql.Contains("$b")) cmd.Parameters.AddWithValue("$b", b.ToString());
                if (sql.Contains("$t")) cmd.Parameters.AddWithValue("$t", b.ToString());
                cmd.ExecuteNonQuery();
            }
        }
    }
}

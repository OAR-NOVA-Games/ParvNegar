using System;
using System.Globalization;

namespace ParvNegar.Domain
{
    /// <summary>
    /// Solar Hijri academic calendar. An academic year starts on 1 Mehr of its start year and
    /// ends on the last day of Khordad of the next year (nine months, SRS AY-01).
    /// Dates are stored as Gregorian calendar dates (DateTime.Date, Kind unspecified) and
    /// presented in Solar Hijri. The internal representation is documented in ARCHITECTURE.md.
    /// </summary>
    public static class AcademicCalendar
    {
        public const int MehrMonth = 7;
        public const int KhordadMonth = 3;
        public const int MinStartYear = 1390;
        public const int MaxStartYear = 1499;

        private static readonly PersianCalendar Persian = new PersianCalendar();

        public static DateTime StartDate(int startSolarYear)
        {
            ValidateStartYear(startSolarYear);
            return Persian.ToDateTime(startSolarYear, MehrMonth, 1, 0, 0, 0, 0).Date;
        }

        public static DateTime EndDate(int startSolarYear)
        {
            ValidateStartYear(startSolarYear);
            int endYear = startSolarYear + 1;
            int lastDay = Persian.GetDaysInMonth(endYear, KhordadMonth);
            return Persian.ToDateTime(endYear, KhordadMonth, lastDay, 0, 0, 0, 0).Date;
        }

        /// <summary>ASCII label such as "1404-1405"; the UI renders Persian digits.</summary>
        public static string Label(int startSolarYear)
        {
            ValidateStartYear(startSolarYear);
            return startSolarYear.ToString(CultureInfo.InvariantCulture) + "-" + (startSolarYear + 1).ToString(CultureInfo.InvariantCulture);
        }

        public static bool Contains(int startSolarYear, DateTime date)
        {
            var d = date.Date;
            return d >= StartDate(startSolarYear) && d <= EndDate(startSolarYear);
        }

        /// <summary>Academic year (start Solar Hijri year) containing the date, or null in the summer gap (Tir–Shahrivar).</summary>
        public static int? AcademicStartYearOf(DateTime date)
        {
            int y = Persian.GetYear(date);
            int m = Persian.GetMonth(date);
            if (m >= MehrMonth) return y;
            if (m <= KhordadMonth) return y - 1;
            return null;
        }

        private static void ValidateStartYear(int startSolarYear)
        {
            if (startSolarYear < MinStartYear || startSolarYear > MaxStartYear)
                throw new DomainException("year.out_of_range", "Academic start year must be between " + MinStartYear + " and " + MaxStartYear + ".");
        }
    }
}

using System;
using System.Globalization;

namespace Orthomania.Patcher.Lazer
{
    /// <summary>
    /// A release of osu!(lazer), such as <c>2026.1026.0</c>. Its NuGet packages carry the bare number, while its installer and GitHub
    /// release may add a stream after a dash (<c>2026.1026.0-lazer</c>, <c>2026.1026.0-tachyon</c>); only the number identifies the build
    /// orthomania is compiled against, so the suffix is kept for display and ignored in comparisons.
    /// </summary>
    public sealed class LazerVersion : IComparable<LazerVersion>, IEquatable<LazerVersion>
    {
        public int Year { get; }

        /// <summary>
        /// The month and day, as lazer writes them (<c>1026</c> for 26 October, <c>702</c> for 2 July).
        /// </summary>
        public int MonthDay { get; }

        public int Patch { get; }

        /// <summary>
        /// What followed the number, without the dash, or empty.
        /// </summary>
        public string Suffix { get; }

        public LazerVersion(int year, int monthDay, int patch, string suffix = "")
        {
            if (year < 0 || monthDay < 0 || patch < 0)
                throw new ArgumentOutOfRangeException(nameof(year), "Version parts cannot be negative.");

            Year = year;
            MonthDay = monthDay;
            Patch = patch;
            Suffix = suffix;
        }

        /// <summary>
        /// The bare number, <c>2026.1026.0</c>: the same text as lazer's NuGet packages, and the name of a version folder.
        /// </summary>
        public string Number => $"{Year}.{MonthDay}.{Patch}";

        public override string ToString() => Suffix.Length > 0 ? $"{Number}-{Suffix}" : Number;

        public static LazerVersion Parse(string text)
            => TryParse(text, out var version) ? version : throw new FormatException($"Not a lazer version: \"{text}\".");

        /// <summary>
        /// Reads <c>2026.1026.0</c>, <c>2026.1026.0-lazer</c>, <c>v2026.1026.0</c> or an assembly version (<c>2026.1026.0.0</c>).
        /// </summary>
        public static bool TryParse(string? text, out LazerVersion version)
        {
            version = null!;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();

            if (text.StartsWith('v') || text.StartsWith('V'))
                text = text[1..];

            string suffix = string.Empty;
            int dash = text.IndexOfAny(new[] { '-', '+' });

            if (dash >= 0)
            {
                suffix = text[(dash + 1)..];
                text = text[..dash];
            }

            string[] parts = text.Split('.');

            // An assembly version has a fourth part, always 0 for lazer.
            if (parts.Length == 4 && parts[3] == "0")
                parts = parts[..3];

            if (parts.Length != 3)
                return false;

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int year)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int monthDay)
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
                return false;

            version = new LazerVersion(year, monthDay, patch, suffix);
            return true;
        }

        public int CompareTo(LazerVersion? other)
        {
            if (other is null)
                return 1;

            int result = Year.CompareTo(other.Year);
            if (result != 0)
                return result;

            result = MonthDay.CompareTo(other.MonthDay);
            return result != 0 ? result : Patch.CompareTo(other.Patch);
        }

        /// <summary>
        /// Same build: the same number, whatever the suffix.
        /// </summary>
        public bool Equals(LazerVersion? other) => other is not null && CompareTo(other) == 0;

        public override bool Equals(object? obj) => obj is LazerVersion other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Year, MonthDay, Patch);

        public static bool operator ==(LazerVersion? a, LazerVersion? b) => a is null ? b is null : a.Equals(b);

        public static bool operator !=(LazerVersion? a, LazerVersion? b) => !(a == b);

        public static bool operator <(LazerVersion a, LazerVersion b) => a.CompareTo(b) < 0;

        public static bool operator >(LazerVersion a, LazerVersion b) => a.CompareTo(b) > 0;

        public static bool operator <=(LazerVersion a, LazerVersion b) => a.CompareTo(b) <= 0;

        public static bool operator >=(LazerVersion a, LazerVersion b) => a.CompareTo(b) >= 0;
    }
}

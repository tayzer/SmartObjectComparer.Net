using System.Globalization;

namespace ParityBench.NET.Plugins;

/// <summary>
/// Orders the free-form version strings plugin manifests declare, so that resolving a
/// plugin without naming a version picks the highest installed one instead of whatever
/// the file system happened to enumerate first.
/// </summary>
/// <remarks>
/// This is deliberately not a semantic-version implementation: it parses nothing into a
/// type, validates nothing and rejects nothing, because manifest versions are
/// unvalidated today. Real semver precedence — where <c>1.0.0-rc1</c> sorts
/// <em>before</em> <c>1.0.0</c> rather than after it, as it does here — belongs with the
/// SDK-version work that also validates the field.
/// </remarks>
internal sealed class PluginVersionOrder : IComparer<string>
{
    public static PluginVersionOrder Instance { get; } = new PluginVersionOrder();

    private PluginVersionOrder()
    {
    }

    public int Compare(string? left, string? right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        // The common case: both are plain dotted numbers, so Version gets it exactly
        // right including differing component counts (1.2 vs 1.2.0).
        if (Version.TryParse(left, out Version? leftVersion) && Version.TryParse(right, out Version? rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        int compared = CompareNumericAware(left, right);

        // Fall back to the raw strings so the comparer is always a total order. Without
        // this, two versions that differ only in a way the walk treats as equal would
        // sort by list position, which is the arbitrary ordering this type exists to
        // remove.
        return compared != 0 ? compared : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walks both strings as alternating runs of digits and non-digits, comparing digit
    /// runs by value so that <c>1.10.0</c> sorts above <c>1.9.0</c>.
    /// </summary>
    private static int CompareNumericAware(string left, string right)
    {
        int leftIndex = 0;
        int rightIndex = 0;

        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            bool leftIsDigit = char.IsAsciiDigit(left[leftIndex]);
            if (leftIsDigit != char.IsAsciiDigit(right[rightIndex]))
            {
                // A number and a word at the same position: order them by character so
                // the result stays stable rather than depending on which side we read.
                return left[leftIndex].CompareTo(right[rightIndex]);
            }

            ReadOnlySpan<char> leftRun = NextRun(left, ref leftIndex, leftIsDigit);
            ReadOnlySpan<char> rightRun = NextRun(right, ref rightIndex, leftIsDigit);

            int compared = leftIsDigit
                ? CompareDigitRuns(leftRun, rightRun)
                : leftRun.CompareTo(rightRun, StringComparison.OrdinalIgnoreCase);

            if (compared != 0)
            {
                return compared;
            }
        }

        // Whichever still has characters left is the longer, and so the higher version.
        return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
    }

    private static ReadOnlySpan<char> NextRun(string value, ref int index, bool digits)
    {
        int start = index;
        while (index < value.Length && char.IsAsciiDigit(value[index]) == digits)
        {
            index++;
        }

        return value.AsSpan(start, index - start);
    }

    private static int CompareDigitRuns(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        // A run long enough to overflow is not a version number anyone wrote on
        // purpose; comparing it textually is good enough and cannot throw.
        return long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out long leftValue)
            && long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out long rightValue)
            ? leftValue.CompareTo(rightValue)
            : left.CompareTo(right, StringComparison.Ordinal);
    }
}

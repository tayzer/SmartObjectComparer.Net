using System.Text;

namespace ParityBench.NET.Domain.Baselines;

/// <summary>
/// The stable identity of a baseline package, independent of its display name so a
/// package keeps working after it is renamed, exported and imported elsewhere.
/// </summary>
public readonly record struct BaselineId
{
    public BaselineId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Baseline id must not be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    /// <summary>
    /// Derives an id from a display name. The id doubles as a directory name, so it
    /// is restricted to characters that are safe on every supported file system.
    /// </summary>
    public static BaselineId FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Baseline name must not be empty.", nameof(name));
        }

        StringBuilder builder = new StringBuilder(name.Length);
        bool lastWasSeparator = false;
        foreach (char character in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        string slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
        {
            throw new ArgumentException("Baseline name must contain at least one letter or digit.", nameof(name));
        }

        return new BaselineId(slug);
    }

    public override string ToString() => Value;
}

namespace ParityBench.NET.Domain.Runs;

public readonly record struct RunId
{
    public RunId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Run identifier must not be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

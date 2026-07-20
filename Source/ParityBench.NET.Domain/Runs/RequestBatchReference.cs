namespace ParityBench.NET.Domain.Runs;

public readonly record struct RequestBatchReference
{
    public RequestBatchReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Request batch reference must not be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

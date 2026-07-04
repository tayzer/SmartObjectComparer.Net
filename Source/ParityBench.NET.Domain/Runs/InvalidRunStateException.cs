namespace ParityBench.NET.Domain.Runs;

public sealed class InvalidRunStateException : InvalidOperationException
{
    public InvalidRunStateException(string message)
        : base(message)
    {
    }
}

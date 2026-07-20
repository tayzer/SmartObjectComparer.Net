namespace ParityBench.NET.Domain.AcceptedDifferences;

public sealed record AcceptedDifferenceFingerprint(
    string Fingerprint,
    string NormalizedPropertyPath,
    string Category,
    string ValueAPattern,
    string ValueBPattern);

namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Represents the result of a deserialization attempt that avoids throwing exceptions
/// for expected failure cases such as SOAP faults, wrong root elements, empty files,
/// or malformed XML. This prevents the VS debugger from breaking on first-chance
/// exceptions during folder comparisons where some files are expected to fail.
/// </summary>
public sealed class DeserializationResult
{
    private DeserializationResult(bool success, object? value, string? errorMessage)
    {
        Success = success;
        Value = value;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets a value indicating whether deserialization succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the deserialized object when <see cref="Success"/> is true.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets the error message when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a successful result containing the deserialized object.
    /// </summary>
    public static DeserializationResult Ok(object value) => new(true, value, null);

    /// <summary>
    /// Creates a failure result with an error message. No exception is thrown.
    /// </summary>
    public static DeserializationResult Failure(string errorMessage) => new(false, null, errorMessage);
}

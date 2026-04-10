namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Categorizes non-success deserialization outcomes so callers can distinguish
/// recognized non-success payloads (for example SOAP faults) from generic failures.
/// </summary>
public enum DeserializationFailureKind
{
    None = 0,
    InvalidInput,
    EmptyPayload,
    MalformedPayload,
    SoapFault,
    RootElementMismatch,
    UnsupportedFormat,
    NullResult,
    DeserializationError,
    UnexpectedError,
}

/// <summary>
/// Represents the result of a deserialization attempt that avoids throwing exceptions
/// for expected failure cases such as SOAP faults, wrong root elements, empty files,
/// or malformed XML. This prevents the VS debugger from breaking on first-chance
/// exceptions during folder comparisons where some files are expected to fail.
/// </summary>
public sealed class DeserializationResult
{
    private DeserializationResult(bool success, object? value, string? errorMessage, DeserializationFailureKind failureKind)
    {
        Success = success;
        Value = value;
        ErrorMessage = errorMessage;
        FailureKind = success ? DeserializationFailureKind.None : failureKind;
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
    /// Gets the structured failure category when <see cref="Success"/> is false.
    /// </summary>
    public DeserializationFailureKind FailureKind { get; }

    /// <summary>
    /// Gets a value indicating whether the failure represents a recognized non-success payload
    /// that can still be compared as raw text instead of treated as a hard error.
    /// </summary>
    public bool IsRecognizedNonSuccessPayload => FailureKind == DeserializationFailureKind.SoapFault;

    /// <summary>
    /// Creates a successful result containing the deserialized object.
    /// </summary>
    public static DeserializationResult Ok(object value) => new(true, value, null, DeserializationFailureKind.None);

    /// <summary>
    /// Creates a failure result with an error message. No exception is thrown.
    /// </summary>
    public static DeserializationResult Failure(
        string errorMessage,
        DeserializationFailureKind failureKind = DeserializationFailureKind.DeserializationError) =>
        new(false, null, errorMessage, failureKind);
}

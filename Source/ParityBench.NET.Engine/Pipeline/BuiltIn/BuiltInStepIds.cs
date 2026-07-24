namespace ParityBench.NET.Engine.Pipeline.BuiltIn;

/// <summary>
/// Step ids for the product's own pipeline steps. They are reserved: a plugin
/// registering a step under one of these ids is rejected by the pipeline builder's
/// duplicate check, so a profile referencing <c>parity.transport.http</c> always
/// means the same thing.
/// </summary>
public static class BuiltInStepIds
{
    public const string SourceRequestLoader = "parity.input.source-request";
    public const string HeaderMerge = "parity.request.headers";
    public const string HttpTransport = "parity.transport.http";
    public const string ResponsePersistence = "parity.response.persist";
    public const string CanonicalMapping = "parity.mapping.canonical";
    public const string CompareNetObjects = "parity.comparison.compare-net-objects";
    public const string FocusedRawContent = "parity.result.focused-raw-content";
}

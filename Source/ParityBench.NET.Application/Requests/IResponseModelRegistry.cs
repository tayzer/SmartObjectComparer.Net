namespace ParityBench.NET.Application.Requests;

/// <summary>
/// Resolves response model names to concrete types used during model-aware comparison.
/// </summary>
public interface IResponseModelRegistry
{
    /// <summary>
    /// Registers a response model type with a stable run option name.
    /// </summary>
    void Register<T>(string modelName) where T : class;

    /// <summary>
    /// Resolves a registered response model name.
    /// </summary>
    Type Resolve(string modelName);

    /// <summary>
    /// Lists the currently registered model names in deterministic order.
    /// </summary>
    IReadOnlyList<string> ListModelNames();
}

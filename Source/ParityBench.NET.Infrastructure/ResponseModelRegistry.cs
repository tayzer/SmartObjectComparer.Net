using ParityBench.NET.Application.Requests;

namespace ParityBench.NET.Infrastructure;

public sealed class ResponseModelRegistry : IResponseModelRegistry
{
    private readonly Dictionary<string, Type> models = new Dictionary<string, Type>(StringComparer.Ordinal);
    private readonly object gate = new object();

    public void Register<T>(string modelName)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("Model name must not be empty.", nameof(modelName));
        }

        string normalizedName = modelName.Trim();
        lock (gate)
        {
            if (models.ContainsKey(normalizedName))
            {
                throw new InvalidOperationException($"Response model '{normalizedName}' is already registered.");
            }

            models[normalizedName] = typeof(T);
        }
    }

    public Type Resolve(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("Model name must not be empty.", nameof(modelName));
        }

        string normalizedName = modelName.Trim();
        lock (gate)
        {
            if (models.TryGetValue(normalizedName, out Type? modelType))
            {
                return modelType;
            }
        }

        throw new InvalidOperationException($"Response model '{normalizedName}' is not registered.");
    }

    public IReadOnlyList<string> ListModelNames()
    {
        lock (gate)
        {
            return models.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
        }
    }
}

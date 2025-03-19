using KellermanSoftware.CompareNetObjects;
using System.Xml.Serialization;
using ComparisonTool.Core;
using System.Text;
using System.Reflection;

/// <summary>
/// Core service for comparing XML file content
/// </summary>
public class XmlComparisonService
{
    private readonly CompareLogic compareLogic;
    private readonly Dictionary<string, Type> registeredDomainModels = new Dictionary<string, Type>();
    private List<IgnoreRule> ignoreRules = new List<IgnoreRule>();

    public XmlComparisonService()
    {
        compareLogic = new CompareLogic
        {
            Config = new ComparisonConfig
            {
                MaxDifferences = 100,
                IgnoreObjectTypes = false,
                ComparePrivateFields = false,
                ComparePrivateProperties = true,
                CompareReadOnly = true
            }
        };
    }

    /// <summary>
    /// Register a domain model for XML deserialization
    /// </summary>
    public void RegisterDomainModel<T>(string modelName) where T : class
    {
        registeredDomainModels[modelName] = typeof(T);
    }

    /// <summary>
    /// Get all registered domain model names
    /// </summary>
    public IEnumerable<string> GetRegisteredModelNames()
    {
        return registeredDomainModels.Keys;
    }

    /// <summary>
    /// Get type for a registered model name
    /// </summary>
    public Type GetModelType(string modelName)
    {
        if (!registeredDomainModels.ContainsKey(modelName))
            throw new ArgumentException($"No model registered with name: {modelName}");

        return registeredDomainModels[modelName];
    }

    /// <summary>
    /// Compare two XML files using the specified domain model
    /// </summary>
    public async Task<ComparisonResult> CompareXmlFilesAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName)
    {
        if (!registeredDomainModels.TryGetValue(modelName, out Type modelType))
            throw new ArgumentException($"No model registered with name: {modelName}");

        var xmlSerializer = new XmlSerializer(typeof(SoapEnvelope));
        oldXmlStream.Position = 0;
        var oldResponse = (SoapEnvelope)xmlSerializer.Deserialize(oldXmlStream);

        newXmlStream.Position = 0;
        var newResponse = (SoapEnvelope)xmlSerializer.Deserialize(newXmlStream);

        // Create clones of the responses that we can modify without affecting the original objects
        var oldResponseCopy = CloneObject(oldResponse);
        var newResponseCopy = CloneObject(newResponse);

        // Process objects to normalize any properties that should be ignored
        var propertiesToIgnore = ignoreRules
            .Where(r => r.IgnoreCompletely)
            .Select(r => GetPropertyNameFromPath(r.PropertyPath))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        // Normalize property values in both object graphs
        if (propertiesToIgnore.Any())
        {
            NormalizePropertyValues(oldResponseCopy, propertiesToIgnore);
            NormalizePropertyValues(newResponseCopy, propertiesToIgnore);
        }

        // Apply other configured settings
        ApplyConfiguredSettings();

        // Compare the normalized objects
        var result = compareLogic.Compare(oldResponseCopy, newResponseCopy);

        var filteredResult = FilterDuplicateDifferences(result);
        return filteredResult;
    }

    /// <summary>
    /// Creates a deep clone of an object using serialization
    /// </summary>
    private T CloneObject<T>(T source)
    {
        if (source == null)
            return default;

        // Serialize to XML and deserialize back to create a clone
        var serializer = new XmlSerializer(typeof(T));
        using var stream = new MemoryStream();
        serializer.Serialize(stream, source);
        stream.Position = 0;
        return (T)serializer.Deserialize(stream);
    }

    /// <summary>
    /// Extract the property name from a path
    /// </summary>
    private string GetPropertyNameFromPath(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
            return string.Empty;

        // If it's already a simple property name, return it
        if (!propertyPath.Contains(".") && !propertyPath.Contains("["))
            return propertyPath;

        // Handle paths with array indices
        if (propertyPath.Contains("["))
        {
            // If it's something like Results[0].Score, extract Score
            var lastDotIndex = propertyPath.LastIndexOf('.');
            if (lastDotIndex >= 0 && lastDotIndex < propertyPath.Length - 1)
                return propertyPath.Substring(lastDotIndex + 1);

            // If it's something like [0].Score, extract Score
            var lastBracketIndex = propertyPath.LastIndexOf(']');
            if (lastBracketIndex >= 0 && lastBracketIndex < propertyPath.Length - 2 &&
                propertyPath[lastBracketIndex + 1] == '.')
                return propertyPath.Substring(lastBracketIndex + 2);
        }

        // For paths like Body.Response.Results.Score, extract Score
        var parts = propertyPath.Split('.');
        return parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
    }

    /// <summary>
    /// Normalize values of specified properties throughout an object graph
    /// </summary>
    private void NormalizePropertyValues(object obj, List<string> propertyNames)
    {
        if (obj == null || propertyNames == null || !propertyNames.Any())
            return;

        // Use reflection to process the object graph
        ProcessObject(obj, propertyNames, new HashSet<object>());
    }

    /// <summary>
    /// Process an object to normalize specified properties
    /// </summary>
    private void ProcessObject(object obj, List<string> propertyNames, HashSet<object> processedObjects)
    {
        // Avoid cycles in the object graph
        if (obj == null || !obj.GetType().IsClass || obj is string || processedObjects.Contains(obj))
            return;

        processedObjects.Add(obj);

        var type = obj.GetType();

        // Process all properties of the object
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Check if this property should be normalized
            if (propertyNames.Contains(property.Name) && property.CanWrite)
            {
                // Set to default value based on property type
                SetDefaultValue(obj, property);
            }
            else if (property.CanRead)
            {
                var value = property.GetValue(obj);

                // If it's a collection, process each item
                if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    foreach (var item in enumerable)
                    {
                        ProcessObject(item, propertyNames, processedObjects);
                    }
                }
                // If it's a complex object, process it recursively
                else if (value != null && value.GetType().IsClass && !(value is string))
                {
                    ProcessObject(value, propertyNames, processedObjects);
                }
            }
        }
    }

    /// <summary>
    /// Set a property to its default/normalized value
    /// </summary>
    private void SetDefaultValue(object obj, PropertyInfo property)
    {
        if (!property.CanWrite)
            return;

        object defaultValue = null;

        var propertyType = property.PropertyType;

        // Handle different property types
        if (propertyType == typeof(string))
        {
            defaultValue = string.Empty;
        }
        else if (propertyType == typeof(int) || propertyType == typeof(int?))
        {
            defaultValue = 0;
        }
        else if (propertyType == typeof(double) || propertyType == typeof(double?))
        {
            defaultValue = 0.0;
        }
        else if (propertyType == typeof(decimal) || propertyType == typeof(decimal?))
        {
            defaultValue = 0m;
        }
        else if (propertyType == typeof(bool) || propertyType == typeof(bool?))
        {
            defaultValue = false;
        }
        else if (propertyType == typeof(DateTime) || propertyType == typeof(DateTime?))
        {
            defaultValue = DateTime.MinValue;
        }
        else if (propertyType == typeof(Guid) || propertyType == typeof(Guid?))
        {
            defaultValue = Guid.Empty;
        }
        else if (propertyType.IsEnum)
        {
            // Use the first enum value
            defaultValue = Enum.GetValues(propertyType).Cast<object>().FirstOrDefault();
        }

        // Set the property to its default value
        try
        {
            property.SetValue(obj, defaultValue);
        }
        catch (Exception)
        {
            // If setting fails, just continue
        }
    }

    /// <summary>
    /// Apply all configured settings from ignore rules
    /// </summary>
    private void ApplyConfiguredSettings()
    {
        // Clear existing config
        compareLogic.Config.MembersToIgnore.Clear();

        // Apply standard ignore rules
        foreach (var rule in ignoreRules)
        {
            if (!rule.IgnoreCompletely)
            {
                // Only apply other aspects of rules like IgnoreCase or IgnoreCollectionOrder
                rule.ApplyTo(compareLogic.Config);
            }
        }
    }

    public async Task<MultiFolderComparisonResult> CompareFoldersAsync(
        List<(Stream Stream, string FileName)> folder1Files,
        List<(Stream Stream, string FileName)> folder2Files,
        string modelName)
    {
        var result = new MultiFolderComparisonResult();

        // Determine how many pairs we can make
        int pairCount = Math.Min(folder1Files.Count, folder2Files.Count);
        result.TotalPairsCompared = pairCount;

        if (pairCount == 0)
        {
            return result;
        }

        // For each pair of files, compare them
        for (int i = 0; i < pairCount; i++)
        {
            var (file1Stream, file1Name) = folder1Files[i];
            var (file2Stream, file2Name) = folder2Files[i];

            // Reset streams to beginning
            file1Stream.Position = 0;
            file2Stream.Position = 0;

            // Do the comparison
            var pairResult = await CompareXmlFilesAsync(file1Stream, file2Stream, modelName);

            // Generate summary
            var categorizer = new DifferenceCategorizer();
            var summary = categorizer.CategorizeAndSummarize(pairResult);

            // Add to results
            var filePairResult = new FilePairComparisonResult
            {
                File1Name = file1Name,
                File2Name = file2Name,
                Result = pairResult,
                Summary = summary
            };

            result.FilePairResults.Add(filePairResult);

            // Update overall equality status
            if (!summary.AreEqual)
            {
                result.AllEqual = false;
            }
        }

        return result;
    }

    private ComparisonResult FilterDuplicateDifferences(ComparisonResult result)
    {
        if (result.Differences.Count <= 1)
            return result;

        // Group differences by their actual values that changed
        var uniqueDiffs = result.Differences
            .GroupBy(d => new
            {
                OldValue = d.Object1Value?.ToString() ?? "null",
                NewValue = d.Object2Value?.ToString() ?? "null"
            })
            .Select(group =>
            {
                // From each group, pick the simplest property path (one without backing fields)
                var bestMatch = group
                    .OrderBy(d => d.PropertyName.Contains("k__BackingField") ? 1 : 0)
                    .ThenBy(d => d.PropertyName.Length)
                    .First();

                return bestMatch;
            })
            .ToList();

        // Clear and replace the differences
        result.Differences.Clear();
        result.Differences.AddRange(uniqueDiffs);

        return result;
    }

    /// <summary>
    /// Configure the comparer to ignore specific properties
    /// </summary>
    public void IgnoreProperty(string propertyPath)
    {
        // Add to our ignore rules
        var rule = new IgnoreRule
        {
            PropertyPath = propertyPath,
            IgnoreCompletely = true
        };

        ignoreRules.Add(rule);
    }

    /// <summary>
    /// Remove a property from the ignore list
    /// </summary>
    public void RemoveIgnoredProperty(string propertyPath)
    {
        // Remove from our rules
        var rulesToRemove = ignoreRules
            .Where(r => r.PropertyPath == propertyPath)
            .ToList();

        foreach (var rule in rulesToRemove)
        {
            ignoreRules.Remove(rule);
        }
    }

    /// <summary>
    /// Get all currently ignored properties
    /// </summary>
    public IReadOnlyList<string> GetIgnoredProperties()
    {
        return ignoreRules
            .Where(r => r.IgnoreCompletely)
            .Select(r => r.PropertyPath)
            .ToList();
    }

    /// <summary>
    /// Configure whether to ignore collection order
    /// </summary>
    public void SetIgnoreCollectionOrder(bool ignoreOrder)
    {
        compareLogic.Config.IgnoreCollectionOrder = ignoreOrder;
    }

    /// <summary>
    /// Get whether collection order is being ignored
    /// </summary>
    public bool GetIgnoreCollectionOrder()
    {
        return compareLogic.Config.IgnoreCollectionOrder;
    }

    /// <summary>
    /// Configure whether to ignore string case
    /// </summary>
    public void SetIgnoreStringCase(bool ignoreCase)
    {
        compareLogic.Config.CaseSensitive = !ignoreCase;
    }

    /// <summary>
    /// Get whether string case is being ignored
    /// </summary>
    public bool GetIgnoreStringCase()
    {
        return !compareLogic.Config.CaseSensitive;
    }

    /// <summary>
    /// Get the current comparison configuration
    /// </summary>
    public ComparisonConfig GetCurrentConfig()
    {
        return compareLogic.Config;
    }
}
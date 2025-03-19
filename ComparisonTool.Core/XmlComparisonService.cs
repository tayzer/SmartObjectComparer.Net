using KellermanSoftware.CompareNetObjects;
using System.Xml.Serialization;
using ComparisonTool.Core;

/// <summary>
/// Core service for comparing XML file content
/// </summary>
public class XmlComparisonService
{
    private readonly CompareLogic compareLogic;
    private readonly Dictionary<string, Type> registeredDomainModels = new Dictionary<string, Type>();

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

        //// Create serializer for the domain model
        //var serializer = new XmlSerializer(modelType);

        //// Deserialize old XML
        //oldXmlStream.Position = 0;
        //var oldObj = serializer.Deserialize(oldXmlStream);

        //// Deserialize new XML
        //newXmlStream.Position = 0;
        //var newObj = serializer.Deserialize(newXmlStream);

        compareLogic.Config.ComparePrivateFields = false;
        compareLogic.Config.CompareReadOnly = true;

        // Compare the objects
        var result2 = compareLogic.Compare(oldResponse.Body.Response.Results, newResponse.Body.Response.Results);

        return FilterDuplicateDifferences(result2);

        return result2;
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
        compareLogic.Config.MembersToIgnore.Add(propertyPath);
    }

    /// <summary>
    /// Remove a property from the ignore list
    /// </summary>
    public void RemoveIgnoredProperty(string propertyPath)
    {
        compareLogic.Config.MembersToIgnore.Remove(propertyPath);
    }

    /// <summary>
    /// Get all currently ignored properties
    /// </summary>
    public IReadOnlyList<string> GetIgnoredProperties()
    {
        return compareLogic.Config.MembersToIgnore;
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
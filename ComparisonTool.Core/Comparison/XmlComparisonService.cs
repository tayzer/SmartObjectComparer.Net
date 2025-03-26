using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Legacy XML comparison service that delegates to the new services for backward compatibility
/// </summary>
public class XmlComparisonService
{
    private readonly ILogger<XmlComparisonService> logger;
    private readonly IXmlDeserializationService deserializationService;
    private readonly IComparisonConfigurationService configService;
    private readonly IComparisonService comparisonService;

    public XmlComparisonService(
        ILogger<XmlComparisonService> logger,
        IXmlDeserializationService deserializationService,
        IComparisonConfigurationService configService,
        IComparisonService comparisonService)
    {
        this.logger = logger;
        this.deserializationService = deserializationService;
        this.configService = configService;
        this.comparisonService = comparisonService;

        this.logger.LogInformation("XmlComparisonService initialized (Legacy facade)");
    }

    /// <summary>
    /// Register a domain model for XML deserialization
    /// </summary>
    public void RegisterDomainModel<T>(string modelName) where T : class
    {
        deserializationService.RegisterDomainModel<T>(modelName);
    }

    /// <summary>
    /// Get all registered domain model names
    /// </summary>
    public IEnumerable<string> GetRegisteredModelNames()
    {
        return deserializationService.GetRegisteredModelNames();
    }

    /// <summary>
    /// Get type for a registered model name
    /// </summary>
    public Type GetModelType(string modelName)
    {
        return deserializationService.GetModelType(modelName);
    }

    /// <summary>
    /// Compare two XML files using the specified domain model
    /// </summary>
    public async Task<ComparisonResult> CompareXmlFilesAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName)
    {
        try
        {
            if (oldXmlStream == null || newXmlStream == null)
            {
                throw new ArgumentNullException(
                    oldXmlStream == null ? nameof(oldXmlStream) : nameof(newXmlStream),
                    "XML stream cannot be null");
            }

            // Delegate to the new comparison service
            return await comparisonService.CompareXmlFilesAsync(
                oldXmlStream,
                newXmlStream,
                modelName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error comparing XML files in legacy service");
            throw;
        }
    }

    /// <summary>
    /// Compare multiple file pairs
    /// </summary>
    public async Task<MultiFolderComparisonResult> CompareFoldersAsync(
        List<(Stream Stream, string FileName)> folder1Files,
        List<(Stream Stream, string FileName)> folder2Files,
        string modelName)
    {
        try
        {
            // Delegate to the new comparison service
            return await comparisonService.CompareFoldersAsync(
                folder1Files,
                folder2Files,
                modelName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error comparing folders in legacy service");
            throw;
        }
    }

    /// <summary>
    /// Configure the comparer to ignore specific properties
    /// </summary>
    public void IgnoreProperty(string propertyPath)
    {
        configService.IgnoreProperty(propertyPath);
    }

    /// <summary>
    /// Remove a property from the ignore list
    /// </summary>
    public void RemoveIgnoredProperty(string propertyPath)
    {
        configService.RemoveIgnoredProperty(propertyPath);
    }

    /// <summary>
    /// Get all currently ignored properties
    /// </summary>
    public IReadOnlyList<string> GetIgnoredProperties()
    {
        return configService.GetIgnoredProperties();
    }

    /// <summary>
    /// Configure whether to ignore collection order
    /// </summary>
    public void SetIgnoreCollectionOrder(bool ignoreOrder)
    {
        configService.SetIgnoreCollectionOrder(ignoreOrder);
    }

    /// <summary>
    /// Get whether collection order is being ignored
    /// </summary>
    public bool GetIgnoreCollectionOrder()
    {
        return configService.GetIgnoreCollectionOrder();
    }

    /// <summary>
    /// Configure whether to ignore string case
    /// </summary>
    public void SetIgnoreStringCase(bool ignoreCase)
    {
        configService.SetIgnoreStringCase(ignoreCase);
    }

    /// <summary>
    /// Get whether string case is being ignored
    /// </summary>
    public bool GetIgnoreStringCase()
    {
        return configService.GetIgnoreStringCase();
    }

    /// <summary>
    /// Get the current comparison configuration
    /// </summary>
    public ComparisonConfig GetCurrentConfig()
    {
        return configService.GetCurrentConfig();
    }
}
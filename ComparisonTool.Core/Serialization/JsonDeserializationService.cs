using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Service responsible for JSON deserialization operations using System.Text.Json
/// </summary>
public class JsonDeserializationService : IDeserializationService
{
    private readonly ILogger<JsonDeserializationService> logger;
    private readonly Dictionary<string, Type> registeredDomainModels = new();
    
    // Cache for recently deserialized objects
    private readonly ConcurrentDictionary<string, (DateTime LastAccess, object Data)> _deserializationCache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(10);
    private readonly int _maxCacheSize = 100;
    private DateTime _lastCacheCleanup = DateTime.Now;
    
    // Application session identifier to invalidate cache on restart
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];

    // JSON serializer options for consistent behavior
    private readonly JsonSerializerOptions _serializerOptions;

    public JsonDeserializationService(ILogger<JsonDeserializationService> logger)
    {
        this.logger = logger;
        
        // Configure JSON serializer options for robust deserialization
        _serializerOptions = new JsonSerializerOptions
        {
            // Handle case-insensitive property names
            PropertyNameCaseInsensitive = true,
            
            // Allow comments in JSON (useful for configuration files)
            ReadCommentHandling = JsonCommentHandling.Skip,
            
            // Allow trailing commas
            AllowTrailingCommas = true,
            
            // Handle numbers as strings and vice versa
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            
            // Include converters for common types
            Converters =
            {
                new JsonStringEnumConverter(), // Handle enums as strings
                new DateTimeConverter(), // Custom DateTime handling
                new NullableConverter(), // Handle nullable types gracefully
                new CollectionConverter() // Ensure compatible collection types for comparison library
            },
            
            // Write indented for debugging
            WriteIndented = true
        };
    }

    /// <summary>
    /// Get supported file formats for this service
    /// </summary>
    public IEnumerable<SerializationFormat> SupportedFormats => new[] { SerializationFormat.Json };

    /// <summary>
    /// Register a domain model for JSON deserialization
    /// </summary>
    /// <typeparam name="T">The type to register</typeparam>
    /// <param name="modelName">Name to identify this model type</param>
    public void RegisterDomainModel<T>(string modelName) where T : class
    {
        registeredDomainModels[modelName] = typeof(T);
        logger.LogInformation("Registered JSON model {ModelName} as {ModelType}", modelName, typeof(T).Name);
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
    /// <param name="modelName">The name of the model to retrieve</param>
    /// <returns>The type associated with the given model name</returns>
    /// <exception cref="ArgumentException">Thrown when no model is registered with the given name</exception>
    public Type GetModelType(string modelName)
    {
        if (!registeredDomainModels.ContainsKey(modelName))
        {
            logger.LogError("No JSON model registered with name: {ModelName}", modelName);
            throw new ArgumentException($"No JSON model registered with name: {modelName}");
        }

        return registeredDomainModels[modelName];
    }

    /// <summary>
    /// Deserialize stream to object with format auto-detection or explicit format
    /// </summary>
    /// <typeparam name="T">Type to deserialize to</typeparam>
    /// <param name="stream">Stream containing the JSON data</param>
    /// <param name="format">Optional explicit format (must be JSON for this service)</param>
    /// <returns>Deserialized object</returns>
    public T Deserialize<T>(Stream stream, SerializationFormat? format = null) where T : class
    {
        if (stream == null)
        {
            logger.LogError("JSON stream cannot be null");
            throw new ArgumentNullException(nameof(stream));
        }

        // Validate format if specified
        if (format.HasValue && format.Value != SerializationFormat.Json)
        {
            throw new ArgumentException($"JsonDeserializationService only supports JSON format, but {format.Value} was specified");
        }

        try
        {
            // Try to determine if we've seen this exact JSON before
            string cacheKey = null;

            if (stream.CanSeek && stream.Length < 1024 * 1024) // Only for reasonably sized files
            {
                // Generate a simple hash as cache key
                using (var ms = new MemoryStream())
                {
                    stream.Position = 0;
                    stream.CopyTo(ms);
                    byte[] bytes = ms.ToArray();
                    var contentHash = Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(bytes));
                    cacheKey = $"json_{contentHash}_{SessionId}"; // Include session ID for cache invalidation

                    // Check cache first
                    if (_deserializationCache.TryGetValue(cacheKey, out var cached))
                    {
                        _deserializationCache[cacheKey] = (DateTime.Now, cached.Data);
                        logger.LogDebug("MD5-based cache HIT for JSON content hash: {CacheKey} (SessionId: {SessionId})", 
                            contentHash[..8], SessionId);
                        return (T)cached.Data;
                    }
                    else
                    {
                        logger.LogDebug("MD5-based cache MISS for JSON content hash: {CacheKey} (SessionId: {SessionId})", 
                            contentHash[..8], SessionId);
                    }

                    // Reset position for deserialization
                    stream.Position = 0;
                }
            }

            // Deserialize JSON using System.Text.Json
            stream.Position = 0;
            var result = JsonSerializer.Deserialize<T>(stream, _serializerOptions);
            
            // DEBUG: Log collection types to identify indexer issues
            if (result != null)
            {
                LogCollectionTypes(result, typeof(T).Name);
                
                // POST-PROCESSING: Convert any problematic collection types to ensure compatibility
                result = ConvertProblematicCollections(result);
            }

            if (result == null)
            {
                logger.LogWarning("JSON deserialization returned null for type {Type}", typeof(T).Name);
                throw new InvalidOperationException($"JSON deserialization returned null for type {typeof(T).Name}");
            }

            // Cache result if needed
            if (cacheKey != null && _deserializationCache.Count < _maxCacheSize)
            {
                _deserializationCache[cacheKey] = (DateTime.Now, result);
                CleanupCacheIfNeeded();
            }

            logger.LogDebug("Successfully deserialized JSON to type {Type}", typeof(T).Name);
            return result;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "JSON parsing error while deserializing to type {Type}: {ErrorMessage}", 
                typeof(T).Name, ex.Message);
            throw new InvalidOperationException($"Invalid JSON format: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deserializing JSON to type {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Clone object efficiently using JSON serialization
    /// </summary>
    /// <typeparam name="T">Type to clone</typeparam>
    /// <param name="source">Source object to clone</param>
    /// <returns>Cloned object</returns>
    public T CloneObject<T>(T source)
    {
        if (source == null)
            return default;

        try
        {
            // Serialize to JSON and then deserialize back
            var json = JsonSerializer.Serialize(source, _serializerOptions);
            var clonedResult = JsonSerializer.Deserialize<T>(json, _serializerOptions);
            
            logger.LogDebug("Successfully cloned object of type {Type} via JSON. JSON size: {JsonSize} characters", 
                typeof(T).Name, json.Length);
            
            return clonedResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cloning object of type {Type} via JSON serialization", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Clear the internal deserialization cache
    /// </summary>
    public void ClearDeserializationCache()
    {
        var count = _deserializationCache.Count;
        _deserializationCache.Clear();
        logger.LogInformation("Cleared internal JSON deserialization cache: {Count} entries removed", count);
    }

    /// <summary>
    /// Get cache statistics for diagnostics
    /// </summary>
    public (int CacheSize, int SerializerCacheSize) GetCacheStatistics()
    {
        // JSON doesn't have a separate serializer cache like XML, so return 0 for that
        return (_deserializationCache.Count, 0);
    }

    /// <summary>
    /// Force clear all caches
    /// </summary>
    public void ClearAllCaches()
    {
        var deserializationCount = _deserializationCache.Count;
        
        _deserializationCache.Clear();
        
        logger.LogWarning("CLEARED ALL JSON CACHES: {DeserializationCache} deserialization entries removed", 
            deserializationCount);
    }

    /// <summary>
    /// Clean up expired cache entries
    /// </summary>
    private void CleanupCacheIfNeeded()
    {
        // Only clean up occasionally
        if ((DateTime.Now - _lastCacheCleanup).TotalMinutes < 5)
            return;

        _lastCacheCleanup = DateTime.Now;

        // Remove expired items
        foreach (var entry in _deserializationCache.ToArray())
        {
            if ((DateTime.Now - entry.Value.LastAccess) > _cacheExpiration)
            {
                _deserializationCache.TryRemove(entry.Key, out _);
            }
        }

        // If still too many entries, remove oldest ones
        if (_deserializationCache.Count > _maxCacheSize)
        {
            var oldestEntries = _deserializationCache
                .OrderBy(x => x.Value.LastAccess)
                .Take(_deserializationCache.Count - _maxCacheSize / 2);

            foreach (var entry in oldestEntries)
            {
                _deserializationCache.TryRemove(entry.Key, out _);
            }
        }
    }
    
    /// <summary>
    /// Debug method to log collection types and identify potential indexer issues
    /// </summary>
    private void LogCollectionTypes(object obj, string context, int depth = 0)
    {
        if (obj == null || depth > 3) return; // Prevent infinite recursion
        
        var type = obj.GetType();
        
        // Check if this object has collections that might cause indexer issues
        foreach (var property in type.GetProperties())
        {
            try
            {
                var value = property.GetValue(obj);
                if (value == null) continue;
                
                var propType = property.PropertyType;
                
                // Log collection types
                if (IsCollectionType(propType))
                {
                    logger.LogDebug("DEBUG COLLECTION: {Context}.{PropertyName} = {PropertyType} (ActualType: {ActualType})", 
                        context, property.Name, propType.Name, value.GetType().Name);
                    
                    // Check if it has proper Count property
                    var countProp = value.GetType().GetProperty("Count");
                    if (countProp != null)
                    {
                        var countValue = countProp.GetValue(value);
                        logger.LogDebug("  -> Count property: {CountType} = {CountValue}", countProp.PropertyType.Name, countValue);
                    }
                    else
                    {
                        logger.LogWarning("  -> NO COUNT PROPERTY FOUND! This could cause indexer errors.");
                    }
                    
                    // Check indexer (be careful - indexers have parameters)
                    var indexer = value.GetType().GetProperty("Item");
                    if (indexer != null)
                    {
                        logger.LogDebug("  -> Has indexer: {IndexerType} (Parameters: {ParameterCount})", 
                            indexer.PropertyType.Name, indexer.GetIndexParameters().Length);
                    }
                }
                
                // Recursively check nested objects (but limit depth)
                if (propType.IsClass && propType != typeof(string) && depth < 2)
                {
                    LogCollectionTypes(value, $"{context}.{property.Name}", depth + 1);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug("Error checking property {PropertyName}: {Error}", property.Name, ex.Message);
            }
        }
    }
    
    private bool IsCollectionType(Type type)
    {
        if (type.IsArray) return true;
        
        if (type.IsGenericType)
        {
            var genericType = type.GetGenericTypeDefinition();
            return genericType == typeof(List<>) ||
                   genericType == typeof(IList<>) ||
                   genericType == typeof(ICollection<>) ||
                   genericType == typeof(IEnumerable<>) ||
                   genericType == typeof(IReadOnlyList<>) ||
                   genericType == typeof(IReadOnlyCollection<>);
        }
        
        return typeof(System.Collections.IList).IsAssignableFrom(type) ||
               typeof(System.Collections.ICollection).IsAssignableFrom(type) ||
               typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }
    
    /// <summary>
    /// Post-process deserialized objects to convert any problematic collection types
    /// that might cause indexer issues with the comparison library
    /// </summary>
    private T ConvertProblematicCollections<T>(T obj)
    {
        if (obj == null) return obj;
        
        try
        {
            var type = obj.GetType();
            
            // If this is a collection type that might cause issues, convert it
            if (IsProblematicCollectionType(type))
            {
                logger.LogDebug("Converting problematic collection type: {TypeName}", type.Name);
                return (T)ConvertToSafeCollection(obj);
            }
            
            // Recursively process all properties
            foreach (var property in type.GetProperties())
            {
                if (property.CanRead && property.CanWrite)
                {
                    var value = property.GetValue(obj);
                    if (value != null && IsProblematicCollectionType(value.GetType()))
                    {
                        var convertedValue = ConvertToSafeCollection(value);
                        property.SetValue(obj, convertedValue);
                        logger.LogDebug("Converted property {PropertyName} from {OriginalType} to {ConvertedType}", 
                            property.Name, value.GetType().Name, convertedValue?.GetType().Name);
                    }
                    else if (value != null && value.GetType().Name == "JsonElement")
                    {
                        // Handle JsonElement properties specifically
                        var convertedValue = ConvertToSafeCollection(value);
                        property.SetValue(obj, convertedValue);
                        logger.LogDebug("Converted JsonElement property {PropertyName} to {ConvertedType}", 
                            property.Name, convertedValue?.GetType().Name);
                    }
                }
            }
            
            return obj;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Error during collection conversion: {Error}", ex.Message);
            return obj;
        }
    }
    
    private bool IsProblematicCollectionType(Type type)
    {
        // Check if it has an indexer but no Count property
        var hasIndexer = type.GetProperty("Item") != null;
        var hasCount = type.GetProperty("Count") != null;
        
        if (hasIndexer && !hasCount)
        {
            logger.LogDebug("Found problematic type: {TypeName} (has indexer but no Count)", type.Name);
            return true;
        }
        
        // Specifically handle JsonElement - this is a common problematic type
        if (type.Name == "JsonElement")
        {
            logger.LogDebug("Found JsonElement type - this needs conversion");
            return true;
        }
        
        // Also check for types that might be collections but aren't properly typed
        if (type == typeof(object) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            return true;
        }
        
        return false;
    }
    
    private object ConvertToSafeCollection(object collection)
    {
        if (collection == null) return null;
        
        try
        {
            // Handle JsonElement specifically - this is a common problematic type
            if (collection.GetType().Name == "JsonElement")
            {
                logger.LogDebug("Converting JsonElement to List<object>");
                var list = new List<object>();
                
                // Try to enumerate the JsonElement
                if (collection is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        list.Add(item);
                    }
                    logger.LogDebug("Converted JsonElement to List<object> with {Count} items", list.Count);
                    return list;
                }
                else
                {
                    // If it's not enumerable, just wrap it in a list
                    list.Add(collection);
                    logger.LogDebug("Wrapped JsonElement in List<object> with 1 item");
                    return list;
                }
            }
            
            // If it's already enumerable, convert to List<object>
            if (collection is System.Collections.IEnumerable enumerableCollection)
            {
                var list = new List<object>();
                foreach (var item in enumerableCollection)
                {
                    list.Add(item);
                }
                logger.LogDebug("Converted to List<object> with {Count} items", list.Count);
                return list;
            }
            
            // If it's a dictionary, convert to Dictionary<object, object>
            if (collection is System.Collections.IDictionary dict)
            {
                var newDict = new Dictionary<object, object>();
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    newDict[entry.Key] = entry.Value;
                }
                logger.LogDebug("Converted to Dictionary<object, object> with {Count} items", newDict.Count);
                return newDict;
            }
            
            // For any other type with an indexer but no Count, wrap it in a list
            logger.LogDebug("Wrapping problematic type {TypeName} in List<object>", collection.GetType().Name);
            return new List<object> { collection };
        }
        catch (Exception ex)
        {
            logger.LogWarning("Error converting collection: {Error}", ex.Message);
            return collection;
        }
    }
}

/// <summary>
/// Custom DateTime converter for JSON to handle various DateTime formats
/// </summary>
public class DateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TryGetDateTime(out var dateTime))
            return dateTime;
            
        // Try to parse as string if direct DateTime parsing fails
        var stringValue = reader.GetString();
        if (DateTime.TryParse(stringValue, out var parsedDateTime))
            return parsedDateTime;
            
        throw new JsonException($"Unable to convert \"{stringValue}\" to DateTime");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }
}

/// <summary>
/// Custom converter for nullable types to handle JSON nulls gracefully
/// </summary>
public class NullableConverter : JsonConverter<object>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return Nullable.GetUnderlyingType(typeToConvert) != null;
    }

    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
            
        var underlyingType = Nullable.GetUnderlyingType(typeToConvert);
        return JsonSerializer.Deserialize(ref reader, underlyingType, options);
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}

/// <summary>
/// Custom JSON converter that ensures collections are created in a way compatible with 
/// KellermanSoftware.Compare-NET-Objects library indexer requirements
/// </summary>
public class CollectionConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        // Skip primitive types, strings, and other non-collection types
        if (typeToConvert.IsPrimitive || 
            typeToConvert == typeof(string) || 
            typeToConvert == typeof(DateTime) ||
            typeToConvert == typeof(decimal) ||
            typeToConvert == typeof(Guid) ||
            typeToConvert.IsEnum)
        {
            return false;
        }
        
        // Handle ALL collection-like types to prevent indexer issues
        
        // Arrays
        if (typeToConvert.IsArray) return true;
        
        // Generic collection types
        if (typeToConvert.IsGenericType)
        {
            var genericType = typeToConvert.GetGenericTypeDefinition();
            return genericType == typeof(List<>) ||
                   genericType == typeof(IList<>) ||
                   genericType == typeof(ICollection<>) ||
                   genericType == typeof(IEnumerable<>) ||
                   genericType == typeof(IReadOnlyList<>) ||
                   genericType == typeof(IReadOnlyCollection<>) ||
                   genericType == typeof(Collection<>) ||
                   genericType == typeof(ObservableCollection<>) ||
                   // Add any other generic collection types that might cause issues
                   genericType.Name.Contains("Collection") ||
                   genericType.Name.Contains("List");
        }
        
        // Non-generic collection types and interfaces
        if (typeof(System.Collections.IList).IsAssignableFrom(typeToConvert) ||
            typeof(System.Collections.ICollection).IsAssignableFrom(typeToConvert) ||
            typeof(System.Collections.IEnumerable).IsAssignableFrom(typeToConvert))
        {
            return true;
        }
        
        // Check for any type that has both indexer and Count properties (potential indexer issue)
        var hasIndexer = typeToConvert.GetProperty("Item") != null;
        var hasCount = typeToConvert.GetProperty("Count") != null;
        
        if (hasIndexer)
        {
            // If it has an indexer, we should probably convert it to ensure compatibility
            return true;
        }
        
        return false;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        // Handle arrays
        if (typeToConvert.IsArray)
        {
            Type elementType = typeToConvert.GetElementType()!;
            Type converterType = typeof(ArrayConverter<>).MakeGenericType(elementType);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }
        
        // Handle generic collection types
        if (typeToConvert.IsGenericType)
        {
            Type elementType = typeToConvert.GetGenericArguments()[0];
            Type converterType = typeof(CollectionConverter<>).MakeGenericType(elementType);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }
        
        // Handle non-generic collections by converting to object collections
        Type objectConverterType = typeof(NonGenericCollectionConverter);
        return (JsonConverter)Activator.CreateInstance(objectConverterType)!;
    }
}

/// <summary>
/// Generic collection converter that ensures List<T> is always created for collections
/// to maintain compatibility with comparison library indexer expectations
/// </summary>
/// <typeparam name="T">Collection element type</typeparam>
public class CollectionConverter<T> : JsonConverter<IList<T>>
{
    public override IList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array");
        }

        // Always create a concrete List<T> to ensure proper Count property and indexer behavior
        var list = new List<T>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            // Deserialize each element
            T? element = JsonSerializer.Deserialize<T>(ref reader, options);
            if (element != null)
            {
                list.Add(element);
            }
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, IList<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        
        foreach (T item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }
        
        writer.WriteEndArray();
    }
}

/// <summary>
/// Array converter that ensures arrays are converted to List<T> for consistent indexer behavior
/// </summary>
/// <typeparam name="T">Array element type</typeparam>
public class ArrayConverter<T> : JsonConverter<T[]>
{
    public override T[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array");
        }

        // Always create a List<T> first, then convert to array
        var list = new List<T>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            T? element = JsonSerializer.Deserialize<T>(ref reader, options);
            if (element != null)
            {
                list.Add(element);
            }
        }

        return list.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        
        foreach (T item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }
        
        writer.WriteEndArray();
    }
}

/// <summary>
/// Non-generic collection converter that ensures proper Count property and indexer behavior
/// </summary>
public class NonGenericCollectionConverter : JsonConverter<System.Collections.IList>
{
    public override System.Collections.IList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array");
        }

        // Always create a List<object> for non-generic collections to ensure proper Count and indexer
        var list = new List<object>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            object? element = JsonSerializer.Deserialize<object>(ref reader, options);
            if (element != null)
            {
                list.Add(element);
            }
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, System.Collections.IList value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        
        foreach (object item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }
        
        writer.WriteEndArray();
    }
}
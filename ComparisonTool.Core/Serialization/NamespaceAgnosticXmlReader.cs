// <copyright file="NamespaceAgnosticXmlReader.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Xml;

namespace ComparisonTool.Core.Serialization;

/// <summary>
/// A custom XmlReader wrapper that makes XML namespace-agnostic for deserialization.
/// Unlike simple namespace stripping, this reader:
/// 1. Reports all elements and attributes as having empty namespace
/// 2. PRESERVES the xsi:nil attribute which is required for nullable types
/// 3. Preserves the xsi:type attribute for polymorphic deserialization
/// 
/// This allows deserializing XML with any namespace (or no namespace) while still
/// correctly handling nullable DateTime?, int?, etc. properties.
/// </summary>
public class NamespaceAgnosticXmlReader : XmlReader
{
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private const string XsiPrefix = "xsi";
    
    private readonly XmlReader innerReader;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="NamespaceAgnosticXmlReader"/> class.
    /// </summary>
    /// <param name="innerReader">The underlying XmlReader to wrap.</param>
    public NamespaceAgnosticXmlReader(XmlReader innerReader)
    {
        this.innerReader = innerReader ?? throw new ArgumentNullException(nameof(innerReader));
    }
    
    // Properties that need special handling for namespace-agnostic behavior
    
    /// <summary>
    /// Gets the namespace URI. Returns empty for all elements except xsi: attributes.
    /// </summary>
    public override string NamespaceURI
    {
        get
        {
            // Preserve xsi namespace for nil and type attributes - these are required for XmlSerializer
            if (IsXsiAttribute())
            {
                return XsiNamespace;
            }
            
            // Return empty namespace for all other elements/attributes
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Gets the namespace prefix. Returns empty for all elements except xsi: attributes.
    /// </summary>
    public override string Prefix
    {
        get
        {
            // Preserve xsi prefix for nil and type attributes
            if (IsXsiAttribute())
            {
                return XsiPrefix;
            }
            
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Checks if the current node is an xsi:nil or xsi:type attribute that should preserve its namespace.
    /// </summary>
    private bool IsXsiAttribute()
    {
        if (innerReader.NodeType != XmlNodeType.Attribute)
        {
            return false;
        }
        
        var localName = innerReader.LocalName;
        var prefix = innerReader.Prefix;
        
        // Check for xsi:nil, xsi:type, and other xsi attributes
        if (string.Equals(prefix, XsiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        // Also check by namespace URI in case prefix varies
        if (string.Equals(innerReader.NamespaceURI, XsiNamespace, StringComparison.Ordinal))
        {
            return true;
        }
        
        return false;
    }
    
    // Pass-through properties
    public override XmlNodeType NodeType => innerReader.NodeType;
    public override string LocalName => innerReader.LocalName;
    public override string Name => innerReader.LocalName; // Use LocalName to strip prefix
    public override string Value => innerReader.Value;
    public override int Depth => innerReader.Depth;
    public override string BaseURI => innerReader.BaseURI;
    public override bool IsEmptyElement => innerReader.IsEmptyElement;
    public override int AttributeCount => innerReader.AttributeCount;
    public override bool EOF => innerReader.EOF;
    public override ReadState ReadState => innerReader.ReadState;
    public override XmlNameTable NameTable => innerReader.NameTable;
    
    // Navigation methods
    public override bool Read() => innerReader.Read();
    public override bool MoveToElement() => innerReader.MoveToElement();
    public override bool MoveToFirstAttribute() => innerReader.MoveToFirstAttribute();
    public override bool MoveToNextAttribute() => innerReader.MoveToNextAttribute();
    public override bool MoveToAttribute(string name) => innerReader.MoveToAttribute(name);
    public override bool MoveToAttribute(string name, string? ns) => innerReader.MoveToAttribute(name, ns);
    public override void MoveToAttribute(int i) => innerReader.MoveToAttribute(i);
    public override bool ReadAttributeValue() => innerReader.ReadAttributeValue();
    
    // Attribute access methods
    public override string? GetAttribute(int i) => innerReader.GetAttribute(i);
    public override string? GetAttribute(string name) => innerReader.GetAttribute(name);
    public override string? GetAttribute(string name, string? namespaceURI) => innerReader.GetAttribute(name, namespaceURI);
    
    // Namespace lookup - report empty namespace but preserve xsi
    public override string? LookupNamespace(string prefix)
    {
        if (string.Equals(prefix, XsiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return XsiNamespace;
        }
        
        return string.Empty;
    }
    
    // Resolve entity
    public override void ResolveEntity() => innerReader.ResolveEntity();
    
    // Close/dispose
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            innerReader.Dispose();
        }
        base.Dispose(disposing);
    }
}

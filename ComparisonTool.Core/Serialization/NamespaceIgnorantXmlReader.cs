// <copyright file="NamespaceIgnorantXmlReader.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Xml;

namespace ComparisonTool.Core.Serialization;

/// <summary>
/// An XmlReader wrapper that strips all XML namespace information during reading.
/// This allows deserialization to work regardless of what namespace is declared in the XML document.
/// </summary>
public class NamespaceIgnorantXmlReader : XmlReader
{
    private readonly XmlReader innerReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamespaceIgnorantXmlReader"/> class.
    /// </summary>
    /// <param name="innerReader">The inner XML reader to wrap.</param>
    public NamespaceIgnorantXmlReader(XmlReader innerReader)
    {
        this.innerReader = innerReader ?? throw new ArgumentNullException(nameof(innerReader));
    }

    /// <inheritdoc/>
    public override string NamespaceURI => string.Empty;

    /// <inheritdoc/>
    public override string Prefix => string.Empty;

    /// <inheritdoc/>
    public override XmlNodeType NodeType => innerReader.NodeType;

    /// <inheritdoc/>
    public override string LocalName => innerReader.LocalName;

    /// <inheritdoc/>
    public override string Value => innerReader.Value;

    /// <inheritdoc/>
    public override int AttributeCount => innerReader.AttributeCount;

    /// <inheritdoc/>
    public override string BaseURI => innerReader.BaseURI;

    /// <inheritdoc/>
    public override int Depth => innerReader.Depth;

    /// <inheritdoc/>
    public override bool EOF => innerReader.EOF;

    /// <inheritdoc/>
    public override bool IsEmptyElement => innerReader.IsEmptyElement;

    /// <inheritdoc/>
    public override XmlNameTable NameTable => innerReader.NameTable;

    /// <inheritdoc/>
    public override ReadState ReadState => innerReader.ReadState;

    /// <inheritdoc/>
    public override string GetAttribute(int i) => innerReader.GetAttribute(i);

    /// <inheritdoc/>
    public override string? GetAttribute(string name) => innerReader.GetAttribute(name);

    /// <inheritdoc/>
    public override string? GetAttribute(string name, string? namespaceURI) => innerReader.GetAttribute(name, string.Empty);

    /// <inheritdoc/>
    public override string LookupNamespace(string prefix) => string.Empty;

    /// <inheritdoc/>
    public override bool MoveToAttribute(string name) => innerReader.MoveToAttribute(name);

    /// <inheritdoc/>
    public override bool MoveToAttribute(string name, string? ns) => innerReader.MoveToAttribute(name, string.Empty);

    /// <inheritdoc/>
    public override bool MoveToElement() => innerReader.MoveToElement();

    /// <inheritdoc/>
    public override bool MoveToFirstAttribute() => innerReader.MoveToFirstAttribute();

    /// <inheritdoc/>
    public override bool MoveToNextAttribute() => innerReader.MoveToNextAttribute();

    /// <inheritdoc/>
    public override bool Read() => innerReader.Read();

    /// <inheritdoc/>
    public override bool ReadAttributeValue() => innerReader.ReadAttributeValue();

    /// <inheritdoc/>
    public override void ResolveEntity() => innerReader.ResolveEntity();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            innerReader.Dispose();
        }

        base.Dispose(disposing);
    }
}

// <copyright file="Models.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Xml.Serialization;

namespace ComparisonTool.Core.Models;

[XmlRoot(ElementName = "Envelope", Namespace = "http://schemas.xmlsoap.org/soap/envelope/")]
public class SoapEnvelope
{
    [XmlElement(ElementName = "Body", Namespace = "http://schemas.xmlsoap.org/soap/envelope/")]
    public SoapBody? Body { get; set; }
}

public class SoapBody
{
    [XmlElement(ElementName = "SearchResponse", Namespace = "urn:soap.co.uk/soap:search1")]
    public SearchResponse? Response { get; set; }
}

public class SearchResponse
{
    [XmlElement(ElementName = "ReportId")]
    public string ReportId { get; set; } = Guid.NewGuid().ToString();

    [XmlElement(ElementName = "GeneratedOn")]
    public DateTime GeneratedOn { get; set; } = DateTime.Now;

    // A summary object with aggregated data.
    [XmlElement(ElementName = "Summary")]
    public Summary Summary { get; set; } = new ();

    // A collection of detailed result items.
    [XmlArray(ElementName = "Results")]
    [XmlArrayItem(ElementName = "Result")]
    public List<ResultItem> Results { get; set; } = new ();

    // A new collection of related items to test property-specific collection order ignoring.
    [XmlArray(ElementName = "RelatedItems")]
    [XmlArrayItem(ElementName = "Item")]
    public List<RelatedItem> RelatedItems { get; set; } = new ();
}

public class Summary
{
    [XmlElement(ElementName = "TotalResults")]
    public int TotalResults { get; set; }

    [XmlElement(ElementName = "SuccessCount")]
    public int SuccessCount { get; set; }

    [XmlElement(ElementName = "FailureCount")]
    public int FailureCount { get; set; }
}

public class ResultItem
{
    [XmlElement(ElementName = "Id")]
    public int Id { get; set; }

    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "Score")]
    public double Score { get; set; }

    // A nested object with further details.
    [XmlElement(ElementName = "Details")]
    public Details Details { get; set; } = new ();

    // A list to represent tags or categories associated with this result.
    [XmlArray(ElementName = "Tags")]
    [XmlArrayItem(ElementName = "Tag")]
    public List<string> Tags { get; set; } = new ();
}

// A new class for related items
public class RelatedItem
{
    [XmlElement(ElementName = "ItemId")]
    public int ItemId { get; set; }

    [XmlElement(ElementName = "ItemName")]
    public string ItemName { get; set; } = string.Empty;

    [XmlElement(ElementName = "Relevance")]
    public double Relevance { get; set; }
}

public class Details
{
    [XmlElement(ElementName = "Description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement(ElementName = "Status")]
    public string Status { get; set; } = string.Empty;
}

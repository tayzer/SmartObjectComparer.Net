using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.ContractProfiles;

using ParityBench.PluginSdk.Comparisons;

namespace ParityBench.NET.Plugins.Tests;

/// <summary>
/// The contract a plugin author declares: what the endpoints accept, and the
/// headers they always need.
/// </summary>
[TestClass]
public sealed class ContractEndpointProfileTests
{
    [TestMethod]
    public void Constructor_WhenContentTypeIsOmitted_LeavesItNullSoTheSourceRequestsOwnIsUsed()
    {
        ContractEndpointProfile profile = new ContractEndpointProfile(PayloadFormat.Xml);

        Assert.IsNull(profile.RequestContentType);
    }

    [TestMethod]
    public void Constructor_WhenContentTypeIsBlank_NormalizesToNullRatherThanAPlaceholder()
    {
        ContractEndpointProfile profile = new ContractEndpointProfile(PayloadFormat.Xml, "   ");

        Assert.IsNull(profile.RequestContentType);
    }

    [TestMethod]
    public void Constructor_WhenContentTypeIsDeclared_TrimsAndKeepsIt()
    {
        ContractEndpointProfile profile = new ContractEndpointProfile(PayloadFormat.Xml, "  text/xml  ");

        Assert.AreEqual("text/xml", profile.RequestContentType);
    }

    [TestMethod]
    public void Constructor_WhenHeadersAreDeclared_CopiesThemCaseInsensitivelyAndDropsBlankKeys()
    {
        ContractEndpointProfile profile = new ContractEndpointProfile(
            PayloadFormat.Xml,
            "text/xml",
            requestHeaders: new Dictionary<string, string>
            {
                ["SOAPAction"] = "urn:lookup",
                ["  "] = "dropped",
            });

        Assert.AreEqual(1, profile.RequestHeaders.Count);
        Assert.AreEqual("urn:lookup", profile.RequestHeaders["soapaction"]);
    }

    [TestMethod]
    public void Constructor_WhenHeadersAreOmitted_ExposesAnEmptyDictionaryRatherThanNull()
    {
        ContractEndpointProfile profile = new ContractEndpointProfile(PayloadFormat.Xml, "text/xml");

        Assert.AreEqual(0, profile.RequestHeaders.Count);
    }

    [TestMethod]
    public void Constructor_WhenHeadersCarryAContentType_ThrowsBecauseItWouldFightRequestContentType()
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => new ContractEndpointProfile(
            PayloadFormat.Xml,
            "text/xml",
            requestHeaders: new Dictionary<string, string> { ["content-type"] = "application/xml" }));

        StringAssert.Contains(error.Message, "requestContentType");
    }
}

[TestClass]
public sealed class SameContractComparisonTests
{
    [TestMethod]
    public void Constructor_WhenOneContractIsDeclared_ServesBothEndpointSlotsFromIt()
    {
        ContractEndpointProfile endpoint = new ContractEndpointProfile(PayloadFormat.Json, "application/json");

        SameContractComparison<CanonicalResponse> comparison = new SameContractComparison<CanonicalResponse>(
            "acme.orders.v1-vs-v2",
            "Orders",
            endpoint);

        Assert.AreSame(endpoint, comparison.EndpointA);
        Assert.AreSame(endpoint, comparison.EndpointB);
        Assert.AreEqual(typeof(CanonicalResponse), comparison.ComparisonType);
    }

    [TestMethod]
    public void Constructor_WhenNothingOptionalIsSupplied_NeedsNoStepsAndNoRules()
    {
        SameContractComparison<CanonicalResponse> comparison = new SameContractComparison<CanonicalResponse>(
            "acme.orders.v1-vs-v2",
            "Orders");

        Assert.AreEqual(0, comparison.DefaultStepIds.Count);
        Assert.AreEqual(0, comparison.RequiredStepIds.Count);
        Assert.IsFalse(comparison.DefaultComparisonRules.HasDefaults);
        Assert.AreEqual("application/json", comparison.Endpoint.RequestContentType);
    }

    [TestMethod]
    public void Constructor_WhenComparisonIdIsBlank_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SameContractComparison<CanonicalResponse>("  ", "Orders"));
    }

    [TestMethod]
    public void Constructor_WhenDisplayNameIsBlank_FallsBackToTheComparisonId()
    {
        SameContractComparison<CanonicalResponse> comparison =
            new SameContractComparison<CanonicalResponse>("acme.orders.v1-vs-v2", "  ");

        Assert.AreEqual("acme.orders.v1-vs-v2", comparison.DisplayName);
    }

    public sealed class CanonicalResponse
    {
        public string Status { get; init; } = string.Empty;
    }
}

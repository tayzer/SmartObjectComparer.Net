using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.AlternateContracts;
using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Infrastructure.Tests;

[TestClass]
public sealed class AlternateContractInfrastructureTests
{
    [TestMethod]
    public void Resolve_WhenProfileIsRegistered_ReturnsProfile()
    {
        AlternateContractProfileRegistry registry = new AlternateContractProfileRegistry();
        IAlternateContractProfile profile = CreateSampleProfile();
        registry.Register(profile);

        IAlternateContractProfile resolved = registry.Resolve(BuiltInAlternateContractProfiles.SampleModelName, BuiltInAlternateContractProfiles.SampleProfileId);

        Assert.AreSame(profile, resolved);
        CollectionAssert.AreEqual(new[] { BuiltInAlternateContractProfiles.SampleProfileId }, registry.GetProfileIds(BuiltInAlternateContractProfiles.SampleModelName).ToArray());
    }

    [TestMethod]
    public void Resolve_WhenProfileIsUnknown_ThrowsInvalidOperationException()
    {
        AlternateContractProfileRegistry registry = new AlternateContractProfileRegistry();

        AssertThrows<InvalidOperationException>(() => registry.Resolve("Missing", "profile-a"));
    }

    [TestMethod]
    public void Resolve_WhenProfileTargetsDifferentModel_ThrowsInvalidOperationException()
    {
        AlternateContractProfileRegistry registry = new AlternateContractProfileRegistry();
        registry.Register(CreateSampleProfile());

        AssertThrows<InvalidOperationException>(() => registry.Resolve("DifferentModel", BuiltInAlternateContractProfiles.SampleProfileId));
    }

    [TestMethod]
    public void Register_WhenProfileIdAlreadyExists_ThrowsInvalidOperationException()
    {
        AlternateContractProfileRegistry registry = new AlternateContractProfileRegistry();
        registry.Register(CreateSampleProfile());

        AssertThrows<InvalidOperationException>(() => registry.Register(CreateSampleProfile()));
    }

    [TestMethod]
    public void Resolve_WhenMultipleProfilesExistForModelWithoutExplicitId_ThrowsInvalidOperationException()
    {
        AlternateContractProfileRegistry registry = new AlternateContractProfileRegistry();
        registry.Register(CreateSimpleProfile("profile-a"));
        registry.Register(CreateSimpleProfile("profile-b"));

        AssertThrows<InvalidOperationException>(() => registry.Resolve("SimpleModel"));
    }

    [TestMethod]
    public async Task SerializeAsync_WhenPayloadIsJson_RoundTripsModel()
    {
        JsonXmlContractPayloadSerializer serializer = new JsonXmlContractPayloadSerializer();
        SampleAlternateJsonCustomerLookupRequest request = new SampleAlternateJsonCustomerLookupRequest
        {
            LookupId = "123",
            RawToken = "tok",
        };

        byte[] bytes = await serializer.SerializeAsync(request, typeof(SampleAlternateJsonCustomerLookupRequest), PayloadFormat.Json);
        using MemoryStream stream = new MemoryStream(bytes);
        object result = await serializer.DeserializeAsync(typeof(SampleAlternateJsonCustomerLookupRequest), stream, PayloadFormat.Json);

        SampleAlternateJsonCustomerLookupRequest roundTripped = (SampleAlternateJsonCustomerLookupRequest)result;
        Assert.AreEqual("123", roundTripped.LookupId);
        Assert.AreEqual("tok", roundTripped.RawToken);
    }

    [TestMethod]
    public async Task SerializeAsync_WhenPayloadIsXml_RoundTripsModel()
    {
        JsonXmlContractPayloadSerializer serializer = new JsonXmlContractPayloadSerializer();
        SampleSoapCustomerLookupRequestEnvelope request = new SampleSoapCustomerLookupRequestEnvelope
        {
            Body = new SampleSoapCustomerLookupRequestBody
            {
                CustomerLookupRequest = new SampleSoapCustomerLookupRequest
                {
                    CustomerId = "123",
                    SensitiveToken = "tok",
                },
            },
        };

        byte[] bytes = await serializer.SerializeAsync(request, typeof(SampleSoapCustomerLookupRequestEnvelope), PayloadFormat.Xml);
        using MemoryStream stream = new MemoryStream(bytes);
        object result = await serializer.DeserializeAsync(typeof(SampleSoapCustomerLookupRequestEnvelope), stream, PayloadFormat.Xml);

        SampleSoapCustomerLookupRequestEnvelope roundTripped = (SampleSoapCustomerLookupRequestEnvelope)result;
        Assert.AreEqual("123", roundTripped.Body.CustomerLookupRequest.CustomerId);
        Assert.AreEqual("tok", roundTripped.Body.CustomerLookupRequest.SensitiveToken);
    }

    [TestMethod]
    public async Task PrepareEndpointBRequestAsync_WhenUsingSampleProfile_ProducesAlternateJsonRequest()
    {
        IAlternateContractProfile profile = CreateSampleProfile();
        byte[] requestBody = Encoding.UTF8.GetBytes(
            "<Envelope><Body><CustomerLookupRequest><CustomerId>123</CustomerId><SensitiveToken>tok</SensitiveToken></CustomerLookupRequest></Body></Envelope>");

        PreparedAlternateContractRequest prepared = await profile.PrepareEndpointBRequestAsync(
            new AlternateContractRequestPreparationContext(
                new RequestItem("one.xml", "application/xml", requestBody.Length),
                requestBody,
                PayloadFormat.Xml));

        string json = Encoding.UTF8.GetString(prepared.Body);
        Assert.AreEqual("application/json", prepared.ContentType);
        Assert.IsTrue(json.Contains("\"lookupId\":\"123\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"raw_token\":\"tok\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task NormalizeEndpointBResponseAsync_WhenUsingSampleProfile_ProducesCanonicalXmlResponse()
    {
        IAlternateContractProfile profile = CreateSampleProfile();
        byte[] responseBody = Encoding.UTF8.GetBytes(
            "{\"statusCode\":\"OK\",\"customerName\":\"Ada\",\"payload\":{\"raw_token\":\"tok\"}}");

        NormalizedAlternateContractResponse normalized = await profile.NormalizeEndpointBResponseAsync(
            new AlternateContractResponseNormalizationContext(
                new RequestItem("one.xml", "application/xml", 1),
                EndpointSlot.B,
                responseBody,
                "application/json",
                PayloadFormat.Json));

        string xml = Encoding.UTF8.GetString(normalized.Body);
        Assert.AreEqual("application/xml", normalized.ContentType);
        Assert.IsTrue(xml.Contains("<CustomerName>Ada</CustomerName>", StringComparison.Ordinal));
        Assert.IsTrue(xml.Contains("<SensitiveToken>tok</SensitiveToken>", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PrepareEndpointBRequestAsync_WhenUsingExpectedProfile_AddsAuthorizationHeader()
    {
        JsonXmlContractPayloadSerializer serializer = new JsonXmlContractPayloadSerializer();
        IAlternateContractProfile profile = BuiltInAlternateContractProfiles.CreateExpectedJsonCustomerLookup(
            serializer,
            new FixedTokenProvider("auth-token"));
        byte[] requestBody = Encoding.UTF8.GetBytes(
            "<Envelope><Body><CustomerLookupRequest><CustomerId>123</CustomerId><AuthenticationToken>seed</AuthenticationToken></CustomerLookupRequest></Body></Envelope>");

        PreparedAlternateContractRequest prepared = await profile.PrepareEndpointBRequestAsync(
            new AlternateContractRequestPreparationContext(
                new RequestItem("one.xml", "application/xml", requestBody.Length),
                requestBody,
                PayloadFormat.Xml));

        Assert.AreEqual("auth-token", prepared.Headers?["AuthorizationToken"]);
        Assert.AreEqual("SourceSystem", profile.DefaultIgnoreRules[0].PropertyPath);
        Assert.AreEqual("customer-lookup/soap", profile.SuggestedEndpointAId);
        Assert.AreEqual("customer-lookup/json", profile.SuggestedEndpointBId);
    }

    private static IAlternateContractProfile CreateSampleProfile() =>
        BuiltInAlternateContractProfiles.CreateSampleSoapToJson(new JsonXmlContractPayloadSerializer());

    private static IAlternateContractProfile CreateSimpleProfile(string profileId) =>
        new AlternateContractProfile<
            SampleSoapCustomerLookupRequestEnvelope,
            SampleAlternateJsonCustomerLookupRequest,
            SampleSoapCustomerLookupResponseEnvelope,
            SampleAlternateJsonCustomerLookupResponse>(
            new JsonXmlContractPayloadSerializer(),
            profileId,
            "SimpleModel",
            _ => new SampleAlternateJsonCustomerLookupRequest(),
            _ => new SampleSoapCustomerLookupResponseEnvelope());

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but got {ex.GetType().Name}.");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    private sealed class FixedTokenProvider : IExpectedJsonCustomerLookupAuthorizationTokenProvider
    {
        private readonly string token;

        public FixedTokenProvider(string token)
        {
            this.token = token;
        }

        public Task<ExpectedJsonCustomerLookupAuthorizationTokenResponse> GetAuthorizationTokensAsync(
            ExpectedJsonCustomerLookupAuthorizationTokenRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExpectedJsonCustomerLookupAuthorizationTokenResponse { AuthorizationToken = token });
    }
}

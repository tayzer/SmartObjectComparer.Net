using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Infrastructure.Tests;

[TestClass]
public sealed class ContractProfileInfrastructureTests
{
    [TestMethod]
    public void Resolve_WhenProfileIsRegistered_ReturnsProfile()
    {
        ContractProfileRegistry registry = new ContractProfileRegistry();
        IContractProfile profile = CreateSampleProfile();
        registry.Register(profile);

        IContractProfile resolved = registry.Resolve(
            BuiltInContractProfiles.SampleModelName,
            new ContractProfileSelection(BuiltInContractProfiles.SampleProfileId));

        Assert.AreSame(profile, resolved);
        CollectionAssert.AreEqual(
            new[] { ContractProfileSelection.SameContractProfileId, BuiltInContractProfiles.SampleProfileId },
            registry.GetProfileIds(BuiltInContractProfiles.SampleModelName).ToArray());
    }

    [TestMethod]
    public void Resolve_WhenProfileIsUnknown_ThrowsInvalidOperationException()
    {
        ContractProfileRegistry registry = new ContractProfileRegistry();

        AssertThrows<InvalidOperationException>(() => registry.Resolve("Missing", new ContractProfileSelection("profile-a")));
    }

    [TestMethod]
    public void Resolve_WhenProfileTargetsDifferentModel_ThrowsInvalidOperationException()
    {
        ContractProfileRegistry registry = new ContractProfileRegistry();
        registry.Register(CreateSampleProfile());

        AssertThrows<InvalidOperationException>(() => registry.Resolve("DifferentModel", new ContractProfileSelection(BuiltInContractProfiles.SampleProfileId)));
    }

    [TestMethod]
    public void Register_WhenProfileIdAlreadyExists_ThrowsInvalidOperationException()
    {
        ContractProfileRegistry registry = new ContractProfileRegistry();
        registry.Register(CreateSampleProfile());

        AssertThrows<InvalidOperationException>(() => registry.Register(CreateSampleProfile()));
    }

    [TestMethod]
    public void Resolve_WhenProfileSelectionIsOmitted_ReturnsSameContractProfile()
    {
        ContractProfileRegistry registry = new ContractProfileRegistry();
        registry.Register(CreateSimpleProfile("profile-a"));
        registry.Register(CreateSimpleProfile("profile-b"));

        IContractProfile profile = registry.Resolve("SimpleModel");

        Assert.AreEqual(ContractProfileSelection.SameContractProfileId, profile.ProfileId);
        Assert.AreEqual("SimpleModel", profile.ResponseModelName);
    }

    [TestMethod]
    public async Task SerializeAsync_WhenJsonPayloadIsWritten_WritesToDestinationStream()
    {
        JsonXmlContractPayloadSerializer serializer = new JsonXmlContractPayloadSerializer();
        SampleAlternateJsonCustomerLookupRequest request = new SampleAlternateJsonCustomerLookupRequest
        {
            LookupId = "123",
            RawToken = "tok",
        };

        using MemoryStream stream = new MemoryStream();
        await serializer.SerializeAsync(request, typeof(SampleAlternateJsonCustomerLookupRequest), PayloadFormat.Json, stream);
        stream.Position = 0;
        object result = await serializer.DeserializeAsync(typeof(SampleAlternateJsonCustomerLookupRequest), stream, PayloadFormat.Json);

        SampleAlternateJsonCustomerLookupRequest roundTripped = (SampleAlternateJsonCustomerLookupRequest)result;
        Assert.AreEqual("123", roundTripped.LookupId);
        Assert.AreEqual("tok", roundTripped.RawToken);
    }

    [TestMethod]
    public async Task SerializeAsync_WhenXmlPayloadIsWritten_WritesToDestinationStream()
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

        using MemoryStream stream = new MemoryStream();
        await serializer.SerializeAsync(request, typeof(SampleSoapCustomerLookupRequestEnvelope), PayloadFormat.Xml, stream);
        stream.Position = 0;
        object result = await serializer.DeserializeAsync(typeof(SampleSoapCustomerLookupRequestEnvelope), stream, PayloadFormat.Xml);

        SampleSoapCustomerLookupRequestEnvelope roundTripped = (SampleSoapCustomerLookupRequestEnvelope)result;
        Assert.AreEqual("123", roundTripped.Body.CustomerLookupRequest.CustomerId);
        Assert.AreEqual("tok", roundTripped.Body.CustomerLookupRequest.SensitiveToken);
    }

    [TestMethod]
    public async Task PrepareRequestAsync_WhenUsingSampleProfile_ProducesEndpointBJsonRequest()
    {
        IContractProfile profile = CreateSampleProfile();
        byte[] requestBody = Encoding.UTF8.GetBytes(
            "<Envelope><Body><CustomerLookupRequest><CustomerId>123</CustomerId><SensitiveToken>tok</SensitiveToken></CustomerLookupRequest></Body></Envelope>");

        PreparedContractRequest prepared = await profile.PrepareRequestAsync(
            EndpointSlot.B,
            new ContractRequestPreparationContext(
                new RequestItem("one.xml", "application/xml", requestBody.Length),
                token => OpenBytesAsync(requestBody, token),
                PayloadFormat.Xml,
                "application/xml"));

        string json = await ReadPayloadAsStringAsync(prepared.Body);
        Assert.AreEqual("application/json", prepared.ContentType);
        Assert.IsTrue(json.Contains("\"lookupId\":\"123\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"raw_token\":\"tok\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task NormalizeResponseAsync_WhenUsingSampleProfile_ProducesCanonicalXmlResponse()
    {
        IContractProfile profile = CreateSampleProfile();
        byte[] responseBody = Encoding.UTF8.GetBytes(
            "{\"statusCode\":\"OK\",\"customerName\":\"Ada\",\"payload\":{\"raw_token\":\"tok\"}}");

        NormalizedContractResponse normalized = await profile.NormalizeResponseAsync(
            EndpointSlot.B,
            new ContractResponseNormalizationContext(
                new RequestItem("one.xml", "application/xml", 1),
                EndpointSlot.B,
                token => OpenBytesAsync(responseBody, token),
                "application/json",
                PayloadFormat.Json));

        string xml = await ReadPayloadAsStringAsync(normalized.Body);
        Assert.AreEqual("application/xml", normalized.ContentType);
        Assert.IsTrue(xml.Contains("<CustomerName>Ada</CustomerName>", StringComparison.Ordinal));
        Assert.IsTrue(xml.Contains("<SensitiveToken>tok</SensitiveToken>", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PrepareRequestAsync_WhenUsingExpectedProfile_AddsAuthorizationHeader()
    {
        JsonXmlContractPayloadSerializer serializer = new JsonXmlContractPayloadSerializer();
        IContractProfile profile = BuiltInContractProfiles.CreateExpectedJsonCustomerLookup(
            serializer,
            new FixedTokenProvider("auth-token"));
        byte[] requestBody = Encoding.UTF8.GetBytes(
            "<Envelope><Body><CustomerLookupRequest><CustomerId>123</CustomerId><AuthenticationToken>seed</AuthenticationToken></CustomerLookupRequest></Body></Envelope>");

        PreparedContractRequest prepared = await profile.PrepareRequestAsync(
            EndpointSlot.B,
            new ContractRequestPreparationContext(
                new RequestItem("one.xml", "application/xml", requestBody.Length),
                token => OpenBytesAsync(requestBody, token),
                PayloadFormat.Xml,
                "application/xml"));

        await prepared.Body.DisposeAsync();
        Assert.AreEqual("auth-token", prepared.Headers?["AuthorizationToken"]);
        Assert.AreEqual("SourceSystem", profile.DefaultIgnoreRules[0].PropertyPath);
        Assert.AreEqual("customer-lookup/soap", profile.EndpointA.SuggestedEndpointId);
        Assert.AreEqual("customer-lookup/json", profile.EndpointB.SuggestedEndpointId);
    }

    [TestMethod]
    public async Task DisposeAsync_WhenPayloadIsFileBacked_RemovesTemporaryFile()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"paritybench-test-{Guid.NewGuid():N}");
        try
        {
            ContractPayloadFactory factory = new ContractPayloadFactory(tempRoot);
            ContractPayload payload = await factory.CreateAsync(
                PayloadFormat.Json,
                "application/json",
                async (destination, cancellationToken) =>
                {
                    byte[] bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                    await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                });
            string payloadPath = Directory.EnumerateFiles(tempRoot).Single();

            await payload.DisposeAsync();

            Assert.IsFalse(File.Exists(payloadPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static IContractProfile CreateSampleProfile() =>
        BuiltInContractProfiles.CreateSampleSoapToJson(new JsonXmlContractPayloadSerializer());

    private static IContractProfile CreateSimpleProfile(string profileId) =>
        new ContractProfile<
            SampleSoapCustomerLookupRequestEnvelope,
            SampleAlternateJsonCustomerLookupRequest,
            SampleSoapCustomerLookupResponseEnvelope,
            SampleAlternateJsonCustomerLookupResponse>(
            new JsonXmlContractPayloadSerializer(),
            profileId,
            "SimpleModel",
            _ => new SampleAlternateJsonCustomerLookupRequest(),
            _ => new SampleSoapCustomerLookupResponseEnvelope());

    private static ValueTask<Stream> OpenBytesAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    private static async Task<string> ReadPayloadAsStringAsync(ContractPayload payload)
    {
        await using (payload)
        await using (Stream stream = await payload.OpenReadAsync().ConfigureAwait(false))
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
        {
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
    }

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
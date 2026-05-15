# Request Comparison Setup Guide

How to integrate your own SOAP/REST domain models into the ComparisonTool request comparison feature.

---

## 1. Overview

The request comparison feature sends the same logical request to **endpoint A** and **endpoint B**, then diffs their responses. Endpoint A always receives the **canonical** request — typically a SOAP/XML payload. Endpoint B can optionally use an **alternate contract**: a different model and serialization format (e.g. a JSON REST API) that the tool translates to/from transparently.

```
Uploaded XML file
       │
       ▼
Deserialize → Canonical Request model
       │                │
       │         Map to Alternate Request model
       │                │
       ▼                ▼
  Endpoint A       Endpoint B (JSON)
       │                │
Canonical Response  Alternate Response
       │                │
       │         Map back to Canonical Response model
       │                │
       ▼                ▼
          Diff / comparison output
```

There are three registration steps. All three are required when adding an alternate-contract pair.

---

## 2. Prerequisites

Add a reference to `ComparisonTool.Core` from any project that contains your registration code:

```xml
<ProjectReference Include="..\ComparisonTool.Core\ComparisonTool.Core.csproj" />
```

Your domain model classes can live anywhere — a dedicated class library is recommended. They do not need to reference `ComparisonTool.Core` directly unless they use framework types.

If you want to study the built-in sample, the sample model classes are in `ComparisonTool.Domain` under `ComparisonTool.Domain.Models`.

---

## 3. Step 1: Define your domain models

You need four C# classes:

| Role | Format | Sent to |
|---|---|---|
| Canonical request | XML (SOAP or plain) | Endpoint A |
| Canonical response | XML | Received from endpoint A; used for comparison output |
| Alternate request | JSON (or other) | Endpoint B |
| Alternate response | JSON (or other) | Received from endpoint B; mapped back before diffing |

### Canonical XML models

Use standard `System.Xml.Serialization` attributes. For SOAP envelopes, decorate the root class with `[XmlRoot]` using the SOAP envelope element name and namespace:

```csharp
using System.Xml.Serialization;

public static class MyServiceNamespaces
{
    public const string SoapEnvelope = "http://schemas.xmlsoap.org/soap/envelope/";
    public const string Service      = "urn:mycompany:myservice";
}

[XmlRoot("Envelope", Namespace = MyServiceNamespaces.SoapEnvelope)]
public class MyRequestEnvelope
{
    [XmlElement("Body", Namespace = MyServiceNamespaces.SoapEnvelope)]
    public MyRequestBody Body { get; set; } = new();
}

public class MyRequestBody
{
    [XmlElement("GetOrderRequest", Namespace = MyServiceNamespaces.Service)]
    public MyGetOrderRequest GetOrderRequest { get; set; } = new();
}

public class MyGetOrderRequest
{
    [XmlElement("OrderId", Namespace = MyServiceNamespaces.Service)]
    public string OrderId { get; set; } = string.Empty;
}

[XmlRoot("Envelope", Namespace = MyServiceNamespaces.SoapEnvelope)]
public class MyResponseEnvelope
{
    [XmlElement("Body", Namespace = MyServiceNamespaces.SoapEnvelope)]
    public MyResponseBody Body { get; set; } = new();
}

public class MyResponseBody
{
    [XmlElement("GetOrderResponse", Namespace = MyServiceNamespaces.Service)]
    public MyGetOrderResponse GetOrderResponse { get; set; } = new();
}

public class MyGetOrderResponse
{
    [XmlElement("OrderStatus", Namespace = MyServiceNamespaces.Service)]
    public string OrderStatus { get; set; } = string.Empty;
}
```

### Alternate (JSON) models

Use `System.Text.Json.Serialization` attributes:

```csharp
using System.Text.Json.Serialization;

public class MyJsonGetOrderRequest
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;
}

public class MyJsonGetOrderResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
```

---

## 4. Step 2: Register the canonical XML model

This call goes **inside** the `AddUnifiedComparisonServices()` (or `AddXmlComparisonServices()`) delegate. It registers the canonical request/response type with the XML deserialization service so the tool can deserialize uploaded request files.

### Most common: SOAP envelope with a different root element name

The CLR class is named `MyRequestEnvelope` but the XML root element is `Envelope`. Use `RegisterDomainModelWithRootElement`:

```csharp
services.AddUnifiedComparisonServices(configuration, options =>
{
    options.RegisterDomainModelWithRootElement<MyRequestEnvelope>(
        modelName:       "MyOrderService",   // shown in the UI dropdown
        rootElementName: "Envelope");        // XML root element, not the C# class name
});
```

### When the CLR type name already matches the XML root element

```csharp
options.RegisterDomainModel<MyOrderRequest>("MyOrderRequest");
```

### When you need full control over the `XmlSerializer`

```csharp
options.RegisterDomainModelWithSerializer<MyRequestEnvelope>(
    modelName: "MyOrderService",
    serializerFactory: () => new XmlSerializer(
        typeof(MyRequestEnvelope),
        new XmlRootAttribute("Envelope")
        {
            Namespace = MyServiceNamespaces.SoapEnvelope
        }));
```

### Namespace handling

`IgnoreXmlNamespaces` defaults to `true`. In this mode the framework uses a namespace-agnostic deserializer, so uploaded XML files deserialize correctly regardless of whether namespaces are present, absent, or differ from what the model declares. Set it to `false` only if you need strict namespace enforcement:

```csharp
options.IgnoreXmlNamespaces = false; // strict — namespaces must match exactly
```

### The `modelName` string

The value you pass as `modelName` is the key the tool uses everywhere: it appears in the comparison panel's model dropdown and it **must exactly match** the `canonicalModelName` you specify in step 4. Choose a stable, descriptive string — it is user-visible.

---

## 5. Step 3: Implement the mapper

Implement `IAlternateContractMapper<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>` from `ComparisonTool.Core.RequestComparison.AlternateContracts`:

```csharp
public interface IAlternateContractMapper<
    TCanonicalRequest,
    TAlternateRequest,
    TCanonicalResponse,
    TAlternateResponse>
    where TCanonicalRequest  : class
    where TAlternateRequest  : class
    where TCanonicalResponse : class
    where TAlternateResponse : class
{
    TAlternateRequest  MapRequest (TCanonicalRequest  canonicalRequest);
    TCanonicalResponse MapResponse(TAlternateResponse alternateResponse);
}
```

Concrete example using the types defined above:

```csharp
using ComparisonTool.Core.RequestComparison.AlternateContracts;

public sealed class MyOrderServiceMapper
    : IAlternateContractMapper<
        MyRequestEnvelope,
        MyJsonGetOrderRequest,
        MyResponseEnvelope,
        MyJsonGetOrderResponse>
{
    public MyJsonGetOrderRequest MapRequest(MyRequestEnvelope canonical)
        => new()
        {
            OrderId = canonical.Body.GetOrderRequest.OrderId,
        };

    public MyResponseEnvelope MapResponse(MyJsonGetOrderResponse alternate)
        => new()
        {
            Body = new MyResponseBody
            {
                GetOrderResponse = new MyGetOrderResponse
                {
                    OrderStatus = alternate.Status,
                },
            },
        };
}
```

The mapper is a plain C# class. You can use any mapping library internally — Mapster, AutoMapper, or manual projection. The interface only constrains the method signatures.

---

## 6. Step 4: Register the alternate contract profile

This call is **separate** from `AddUnifiedComparisonServices`. It registers the profile with the DI container as a singleton that the comparison pipeline picks up at runtime.

### Form 1 — Type-parameter (mapper activated via `new()`)

Use this when your mapper has a public parameterless constructor:

```csharp
services.AddRequestComparisonAlternateContractProfiles(options =>
    options.RegisterAlternateContract<
        MyRequestEnvelope,
        MyJsonGetOrderRequest,
        MyResponseEnvelope,
        MyJsonGetOrderResponse,
        MyOrderServiceMapper>(
        canonicalModelName: "MyOrderService",   // must match step 2
        profileId:          "my-order-soap-to-json",
        configure: builder => builder
            .SupportSourceRequestFormats(SerializationFormat.Xml)
            .UseAlternateRequestFormat(SerializationFormat.Json, "application/json")
            .UseAlternateResponseFormat(SerializationFormat.Json)));
```

### Form 2 — Instance (mapper needs constructor arguments)

```csharp
services.AddRequestComparisonAlternateContractProfiles(options =>
    options.RegisterAlternateContract<
        MyRequestEnvelope,
        MyJsonGetOrderRequest,
        MyResponseEnvelope,
        MyJsonGetOrderResponse>(
        canonicalModelName: "MyOrderService",
        profileId:          "my-order-soap-to-json",
        mapper:             new MyOrderServiceMapper(somedependency),
        configure: builder => builder
            .SupportSourceRequestFormats(SerializationFormat.Xml)
            .UseAlternateRequestFormat(SerializationFormat.Json, "application/json")
            .UseAlternateResponseFormat(SerializationFormat.Json)));
```

### Form 3 — Raw delegates (quick one-offs or tests)

```csharp
services.AddRequestComparisonAlternateContractProfiles(options =>
    options.RegisterProfile<
        MyRequestEnvelope,
        MyJsonGetOrderRequest,
        MyResponseEnvelope,
        MyJsonGetOrderResponse>(
        canonicalModelName: "MyOrderService",
        profileId:          "my-order-soap-to-json",
        requestMapper:  req  => new MyJsonGetOrderRequest  { OrderId = req.Body.GetOrderRequest.OrderId },
        responseMapper: resp => new MyResponseEnvelope
        {
            Body = new MyResponseBody
            {
                GetOrderResponse = new MyGetOrderResponse { OrderStatus = resp.Status },
            },
        }));
```

### Builder options reference

| Method | Purpose | Default |
|---|---|---|
| `SupportSourceRequestFormats(params formats)` | Formats the tool accepts for uploaded request files | `Xml` |
| `UseAlternateRequestFormat(format, contentType?)` | Format and `Content-Type` used when POSTing to endpoint B | `Json` / `application/json` |
| `UseAlternateResponseFormat(format)` | Format expected in endpoint B's response body | `Json` |
| `MapCanonicalResponsePropertyPath(canonicalPath, alternatePath)` | When a field is masked, maps the canonical dot-path to the raw path in endpoint B's response. Call once per masked field. | — |
| `UseCanonicalRequestDeserializer(Func<Stream, SerializationFormat, T>)` | Override how the uploaded source request is deserialized into `TCanonicalRequest` | Built-in XML deserializer |
| `UseAlternateRequestSerializer(Func<T, byte[]>, contentType?)` | Override how `TAlternateRequest` is serialized for the endpoint B HTTP body | Built-in JSON serializer |
| `UseAlternateResponseDeserializer(Func<Stream, string?, T>)` | Override how endpoint B's response body is deserialized into `TAlternateResponse` | Built-in JSON deserializer |
| `UseCanonicalResponseSerializer(Func<T, byte[]>)` | Override how `TCanonicalResponse` is serialized for comparison output | Built-in XML serializer |

---

## 7. How the pieces connect

```
Step 2                              Step 4
RegisterDomainModelWithRootElement  RegisterAlternateContract
  modelName: "MyOrderService"  ──►  canonicalModelName: "MyOrderService"
```

The `canonicalModelName` string is the only link between the two registrations. If the strings do not match exactly (case-sensitive), the tool will not find the alternate contract profile when that model is selected.

### Call-order requirements

- **Step 2 must be inside** the `AddUnifiedComparisonServices` (or `AddXmlComparisonServices`) delegate. Calling it after the method returns has no effect.
- **Step 4 must be a separate** `AddRequestComparisonAlternateContractProfiles` call. It does not go inside `AddUnifiedComparisonServices`.
- The two calls can appear in any order relative to each other — they both register singletons that are resolved at request time.

---

## 8. Using the feature in the UI

Once the registration is in place and the host restarts:

1. Open the **Request Comparison** panel.
2. Enable **"Use Alternate Contract for Endpoint B"**.
3. Select your profile from the **alternate contract profile** dropdown (identified by the `profileId` you registered).
4. Upload an XML request file and run the comparison as normal.

The tool deserializes the uploaded file into `TCanonicalRequest`, maps it to `TAlternateRequest`, sends it to endpoint B, receives `TAlternateResponse`, maps it back to `TCanonicalResponse`, and then diffs the two canonical responses.

---

## 9. Complete end-to-end example

Everything in one place, ready to copy and adapt.

### Models

```csharp
// MyServiceModels.cs
using System.Text.Json.Serialization;
using System.Xml.Serialization;

public static class MyServiceNamespaces
{
    public const string SoapEnvelope = "http://schemas.xmlsoap.org/soap/envelope/";
    public const string Service      = "urn:mycompany:myservice";
}

// --- Canonical XML ---

[XmlRoot("Envelope", Namespace = MyServiceNamespaces.SoapEnvelope)]
public class MyRequestEnvelope
{
    [XmlElement("Body", Namespace = MyServiceNamespaces.SoapEnvelope)]
    public MyRequestBody Body { get; set; } = new();
}

public class MyRequestBody
{
    [XmlElement("GetOrderRequest", Namespace = MyServiceNamespaces.Service)]
    public MyGetOrderRequest GetOrderRequest { get; set; } = new();
}

public class MyGetOrderRequest
{
    [XmlElement("OrderId",    Namespace = MyServiceNamespaces.Service)]
    public string OrderId    { get; set; } = string.Empty;

    [XmlElement("AuthToken",  Namespace = MyServiceNamespaces.Service)]
    public string AuthToken  { get; set; } = string.Empty;
}

[XmlRoot("Envelope", Namespace = MyServiceNamespaces.SoapEnvelope)]
public class MyResponseEnvelope
{
    [XmlElement("Body", Namespace = MyServiceNamespaces.SoapEnvelope)]
    public MyResponseBody Body { get; set; } = new();
}

public class MyResponseBody
{
    [XmlElement("GetOrderResponse", Namespace = MyServiceNamespaces.Service)]
    public MyGetOrderResponse GetOrderResponse { get; set; } = new();
}

public class MyGetOrderResponse
{
    [XmlElement("OrderStatus", Namespace = MyServiceNamespaces.Service)]
    public string OrderStatus { get; set; } = string.Empty;

    [XmlElement("AuthToken",   Namespace = MyServiceNamespaces.Service)]
    public string AuthToken   { get; set; } = string.Empty;
}

// --- Alternate JSON ---

public class MyJsonGetOrderRequest
{
    [JsonPropertyName("orderId")]
    public string OrderId   { get; set; } = string.Empty;

    [JsonPropertyName("auth_token")]
    public string AuthToken { get; set; } = string.Empty;
}

public class MyJsonGetOrderResponse
{
    [JsonPropertyName("status")]
    public string Status    { get; set; } = string.Empty;

    [JsonPropertyName("auth_token")]
    public string AuthToken { get; set; } = string.Empty;
}
```

### Mapper

```csharp
// MyOrderServiceMapper.cs
using ComparisonTool.Core.RequestComparison.AlternateContracts;

public sealed class MyOrderServiceMapper
    : IAlternateContractMapper<
        MyRequestEnvelope,
        MyJsonGetOrderRequest,
        MyResponseEnvelope,
        MyJsonGetOrderResponse>
{
    public MyJsonGetOrderRequest MapRequest(MyRequestEnvelope canonical)
        => new()
        {
            OrderId   = canonical.Body.GetOrderRequest.OrderId,
            AuthToken = canonical.Body.GetOrderRequest.AuthToken,
        };

    public MyResponseEnvelope MapResponse(MyJsonGetOrderResponse alternate)
        => new()
        {
            Body = new MyResponseBody
            {
                GetOrderResponse = new MyGetOrderResponse
                {
                    OrderStatus = alternate.Status,
                    AuthToken   = alternate.AuthToken,
                },
            },
        };
}
```

### Registration (in any host: Web, Desktop, CLI)

```csharp
using ComparisonTool.Core.DI;
using ComparisonTool.Core.RequestComparison.AlternateContracts;
using ComparisonTool.Core.Serialization;

// Step 2 — inside AddUnifiedComparisonServices:
services.AddUnifiedComparisonServices(configuration, options =>
{
    // Existing registrations ...
    options.RegisterDomainModelWithRootElement<MyRequestEnvelope>(
        modelName:       "MyOrderService",
        rootElementName: "Envelope");
});

// Step 4 — separate call:
services.AddRequestComparisonAlternateContractProfiles(options =>
    options.RegisterAlternateContract<
        MyRequestEnvelope,
        MyJsonGetOrderRequest,
        MyResponseEnvelope,
        MyJsonGetOrderResponse,
        MyOrderServiceMapper>(
        canonicalModelName: "MyOrderService",
        profileId:          "my-order-soap-to-json",
        configure: builder => builder
            .SupportSourceRequestFormats(SerializationFormat.Xml)
            .UseAlternateRequestFormat(SerializationFormat.Json, "application/json")
            .UseAlternateResponseFormat(SerializationFormat.Json)
            // Map the canonical AuthToken path to the endpoint B path for masking:
            .MapCanonicalResponsePropertyPath(
                "Envelope.Body.GetOrderResponse.AuthToken",
                "auth_token")));
```

For Web, this goes in `ComparisonTool.Web/Program.cs`. For Desktop, in `ComparisonTool.Desktop/App.xaml.cs`. For CLI, in `ComparisonTool.Cli/Infrastructure/ServiceProviderFactory.cs`. All three follow the same pattern — add alongside the existing registrations.

---

## 10. Advanced: overriding serialization

The four `Use*` methods on the builder let you bypass the built-in serializers when the defaults are insufficient.

### When to override

| Scenario | Method to use |
|---|---|
| Uploaded XML uses a non-standard encoding or wrapper that the default deserializer mishandles | `UseCanonicalRequestDeserializer` |
| Endpoint B requires a non-standard body format (e.g. form-encoded, custom envelope) | `UseAlternateRequestSerializer` |
| Endpoint B returns a non-standard response that `System.Text.Json` cannot deserialize automatically | `UseAlternateResponseDeserializer` |
| The canonical response XML must be produced with specific formatting or encoding for a downstream consumer | `UseCanonicalResponseSerializer` |

### Example: custom canonical request deserializer

```csharp
configure: builder => builder
    .UseCanonicalRequestDeserializer((stream, format) =>
    {
        // e.g. strip a non-standard BOM before deserializing
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var xml = reader.ReadToEnd();
        var serializer = new XmlSerializer(typeof(MyRequestEnvelope),
            new XmlRootAttribute("Envelope"));
        using var sr = new StringReader(xml);
        return (MyRequestEnvelope)serializer.Deserialize(sr)!;
    })
```

### Example: custom alternate request serializer

```csharp
configure: builder => builder
    .UseAlternateRequestSerializer(
        serializer: req => JsonSerializer.SerializeToUtf8Bytes(req, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        }),
        contentType: "application/json; charset=utf-8")
```

Override only what you need — any method you do not call leaves the built-in default in place.

---

## Repo sample

The repo ships a working reference implementation in:

- **Models**: `ComparisonTool.Domain/Models/RequestComparisonSampleAlternateContractModels.cs`
- **Registration**: `ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractSampleRegistration.cs`

The sample is already wired into all three hosts and can serve as a live end-to-end reference.

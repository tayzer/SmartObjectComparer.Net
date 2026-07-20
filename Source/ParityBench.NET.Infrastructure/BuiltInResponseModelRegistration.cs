using ParityBench.NET.Application.Requests;

namespace ParityBench.NET.Infrastructure;

public static class BuiltInResponseModelRegistration
{
    public static void Register(IResponseModelRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register<SampleSoapCustomerLookupResponseEnvelope>(BuiltInContractProfiles.SampleModelName);
        registry.Register<ExpectedJsonCustomerLookupResponse>(BuiltInContractProfiles.ExpectedJsonCustomerLookupModelName);
    }
}


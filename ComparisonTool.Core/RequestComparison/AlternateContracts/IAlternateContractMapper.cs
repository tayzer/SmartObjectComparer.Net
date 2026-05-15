namespace ComparisonTool.Core.RequestComparison.AlternateContracts;

/// <summary>
/// Translates between canonical and alternate contract models for endpoint B comparison.
/// </summary>
public interface IAlternateContractMapper<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>
    where TCanonicalRequest : class
    where TAlternateRequest : class
    where TCanonicalResponse : class
    where TAlternateResponse : class
{
    /// <summary>Maps a canonical request to the alternate request format for endpoint B.</summary>
    TAlternateRequest MapRequest(TCanonicalRequest canonicalRequest);

    /// <summary>Maps an endpoint B alternate response back to the canonical response format.</summary>
    TCanonicalResponse MapResponse(TAlternateResponse alternateResponse);
}

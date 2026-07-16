using ComparisonTool.Core.Serialization;

namespace ComparisonTool.Core.RequestComparison.AlternateContracts;

/// <summary>
/// Collects alternate contract profile registrations that are applied during DI setup.
/// </summary>
public sealed class RequestComparisonAlternateContractOptions
{
    private readonly List<RequestComparisonAlternateContractProfile> profiles = new();

    internal IReadOnlyList<RequestComparisonAlternateContractProfile> Profiles => profiles;

    /// <summary>
    /// Registers a strongly typed alternate contract profile.
    /// </summary>
    public RequestComparisonAlternateContractOptions RegisterProfile<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>(
        string canonicalModelName,
        string profileId,
        Func<TCanonicalRequest, TAlternateRequest> requestMapper,
        Func<TAlternateResponse, TCanonicalResponse> responseMapper,
        Action<RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>>? configure = null)
        where TCanonicalRequest : class
        where TAlternateRequest : class
        where TCanonicalResponse : class
        where TAlternateResponse : class
    {
        var builder = new RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>();
        configure?.Invoke(builder);
        profiles.Add(builder.Build(canonicalModelName, profileId, requestMapper, responseMapper));
        return this;
    }

    /// <summary>
    /// Registers a prebuilt profile instance.
    /// </summary>
    public RequestComparisonAlternateContractOptions RegisterProfile(RequestComparisonAlternateContractProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        profiles.Add(profile);
        return this;
    }

    /// <summary>
    /// Registers a strongly typed alternate contract profile using an <see cref="IAlternateContractMapper{TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse}"/> instance.
    /// </summary>
    /// <param name="canonicalModelName">The canonical comparison model name this profile targets.</param>
    /// <param name="profileId">The unique identifier for this profile.</param>
    /// <param name="mapper">The mapper instance that translates between canonical and alternate contract models.</param>
    /// <param name="configure">Optional builder delegate for additional profile settings.</param>
    public RequestComparisonAlternateContractOptions RegisterAlternateContract<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>(
        string canonicalModelName,
        string profileId,
        IAlternateContractMapper<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse> mapper,
        Action<RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>>? configure = null)
        where TCanonicalRequest : class
        where TAlternateRequest : class
        where TCanonicalResponse : class
        where TAlternateResponse : class
    {
        return RegisterProfile(
            canonicalModelName,
            profileId,
            requestMapper: mapper.MapRequest,
            responseMapper: mapper.MapResponse,
            configure);
    }

    /// <summary>
    /// Registers a strongly typed alternate contract profile by activating a mapper of type <typeparamref name="TMapper"/>
    /// using its public parameterless constructor.
    /// </summary>
    /// <typeparam name="TCanonicalRequest">The canonical request type.</typeparam>
    /// <typeparam name="TAlternateRequest">The alternate request type sent to endpoint B.</typeparam>
    /// <typeparam name="TCanonicalResponse">The canonical response type used for comparison.</typeparam>
    /// <typeparam name="TAlternateResponse">The alternate response type returned by endpoint B.</typeparam>
    /// <typeparam name="TMapper">
    /// The mapper type implementing
    /// <see cref="IAlternateContractMapper{TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse}"/>.
    /// Must have a public parameterless constructor.
    /// </typeparam>
    /// <param name="canonicalModelName">The canonical comparison model name this profile targets.</param>
    /// <param name="profileId">The unique identifier for this profile.</param>
    /// <param name="configure">Optional builder delegate for additional profile settings.</param>
    public RequestComparisonAlternateContractOptions RegisterAlternateContract<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse, TMapper>(
        string canonicalModelName,
        string profileId,
        Action<RequestComparisonAlternateContractProfileBuilder<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>>? configure = null)
        where TCanonicalRequest : class
        where TAlternateRequest : class
        where TCanonicalResponse : class
        where TAlternateResponse : class
        where TMapper : IAlternateContractMapper<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>, new()
    {
        var mapper = new TMapper();
        return RegisterAlternateContract(canonicalModelName, profileId, mapper, configure);
    }
}

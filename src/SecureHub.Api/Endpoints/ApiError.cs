using Microsoft.Extensions.Options;
using SecureHub.Core.Domain;
using SecureHub.Core.Localization;
using SecureHub.Core.Options;

namespace SecureHub.Api.Endpoints;

public static class ApiLocalization
{
    public static ILocalizer For(HttpContext http, IOptions<SecureHubOptions> options) =>
        new DictionaryLocalizer(DictionaryLocalizer.Negotiate(
            http.Request.Headers.AcceptLanguage.ToString(),
            options.Value.Localization.Default));
}

public static class ApiError
{
    public static IResult Create(
        int status,
        string code,
        ILocalizer localizer,
        string? requestId = null,
        string? suggestedModel = null,
        IEnumerable<string>? candidates = null)
    {
        var key = code.Contains('.', StringComparison.Ordinal) ? code : $"error.{code}";
        var message = localizer.Get(key);

        object payload = new
        {
            error = new
            {
                code,
                message,
                request_id = requestId,
                suggested_model = suggestedModel,
                candidates
            }
        };

        return Results.Json(payload, statusCode: status);
    }

    public static IResult NotFound(ILocalizer localizer) =>
        Create(StatusCodes.Status404NotFound, ReasonCodes.ModelNotFound, localizer);

    public static IResult Unauthorized(ILocalizer localizer) =>
        Create(StatusCodes.Status401Unauthorized, ReasonCodes.InvalidApiKey, localizer);

    public static IResult Forbidden(ILocalizer localizer, string code) =>
        Create(StatusCodes.Status403Forbidden, code, localizer);
}

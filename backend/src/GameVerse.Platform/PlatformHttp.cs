using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GameVerse.Platform;

/// <summary>
/// HTTP plumbing shared by every module. Lives at the project root because the architecture tests allow only module
/// folders under GameVerse.Platform.
/// </summary>
public static class PlatformHttp
{
    /// <summary>Rate-limit policy names (registered by the host, CLAUDE.md section 7).</summary>
    public static class RateLimits
    {
        public const string SignIn = "auth-sign-in";
        public const string Create = "auth-create";
        public const string Email = "auth-email";
    }

    /// <summary>
    /// Validates the request body of type <typeparamref name="T"/> with its FluentValidation validator and answers 400
    /// ValidationProblem (problem+json with field errors) before the handler runs.
    /// </summary>
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder)
        where T : class =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var body = context.Arguments.OfType<T>().FirstOrDefault();
            var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
            if (body is not null && validator is not null)
            {
                var result = await validator.ValidateAsync(body, context.HttpContext.RequestAborted).ConfigureAwait(false);
                if (!result.IsValid)
                {
                    return Results.ValidationProblem(result.ToDictionary());
                }
            }

            return await next(context).ConfigureAwait(false);
        }).ProducesValidationProblem();

    /// <summary>401 with a deliberately vague message: never say which part of a credential was wrong.</summary>
    public static IResult InvalidCredentials() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed", detail: "The credentials are not valid.");
}

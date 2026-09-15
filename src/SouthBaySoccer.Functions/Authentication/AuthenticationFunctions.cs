using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Functions.Onboarding;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Functions.Pipeline.RateLimiting;

namespace SouthBaySoccer.Functions.Authentication;

/// <summary>
/// Sign-in, sign-up, and session endpoints. Every anonymous endpoint is rate limited per caller IP
/// before any request body is read; sign-in and email checks are additionally limited per input.
/// </summary>
public sealed class AuthenticationFunctions(
    IAuthenticationWorkflow authenticationWorkflow,
    IOnboardingWorkflow onboardingWorkflow,
    IAnonymousRateLimiter rateLimiter)
{
    [Function(nameof(SignInByPhone))]
    [AllowAnonymous]
    public async Task<HttpResponseData> SignInByPhone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/pickuppal/phone/sign-in")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        LimitByIp(request);
        var body = await ReadRequiredJsonAsync<SignInByPhoneRequest>(request, cancellationToken);
        rateLimiter.EnsureAllowed(AnonymousRateLimits.PerPhone, AnonymousRateLimits.PhoneKey(body.PhoneNumber ?? string.Empty));

        var result = await authenticationWorkflow.BeginPhoneSignInAsync(body, cancellationToken);
        // 202: the sign-in is accepted but not complete until the WhatsApp login link comes back.
        var statusCode = result.VerificationRequired ? HttpStatusCode.Accepted : HttpStatusCode.OK;
        return await WriteJsonAsync(request, statusCode, result, cancellationToken);
    }

    [Function(nameof(CompleteWhatsAppLogin))]
    [AllowAnonymous]
    public async Task<HttpResponseData> CompleteWhatsAppLogin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/pickuppal/login/complete")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        LimitByIp(request);
        var body = await ReadRequiredJsonAsync<CompleteWhatsAppLoginRequest>(request, cancellationToken);

        var result = await authenticationWorkflow.CompleteWhatsAppLoginAsync(body, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function(nameof(ValidateRegistrationToken))]
    [AllowAnonymous]
    public async Task<HttpResponseData> ValidateRegistrationToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/pickuppal/register/validate")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        LimitByIp(request);
        var body = await ReadRequiredJsonAsync<ValidateRegistrationTokenRequest>(request, cancellationToken);

        var result = await onboardingWorkflow.ValidateRegistrationTokenAsync(body, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function(nameof(CheckEmailAvailability))]
    [AllowAnonymous]
    public async Task<HttpResponseData> CheckEmailAvailability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/pickuppal/register/email-availability")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        LimitByIp(request);
        var body = await ReadRequiredJsonAsync<CheckEmailAvailabilityRequest>(request, cancellationToken);
        rateLimiter.EnsureAllowed(AnonymousRateLimits.PerEmail, AnonymousRateLimits.InputKey(body.Email ?? string.Empty));

        var result = await onboardingWorkflow.CheckEmailAvailabilityAsync(body, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function(nameof(RegisterWithWhatsApp))]
    [AllowAnonymous]
    public async Task<HttpResponseData> RegisterWithWhatsApp(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/pickuppal/register")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        LimitByIp(request);
        var body = await ReadRequiredJsonAsync<RegisterWithWhatsAppRequest>(request, cancellationToken);

        var result = await onboardingWorkflow.RegisterWithWhatsAppAsync(body, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.Created, result, cancellationToken);
    }

    [Function(nameof(GetCurrentTermsVersion))]
    [AllowAnonymous]
    public async Task<HttpResponseData> GetCurrentTermsVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/terms/current")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        LimitByIp(request);

        return await WriteJsonAsync(request, HttpStatusCode.OK, onboardingWorkflow.GetCurrentTermsVersion(), cancellationToken);
    }

    [Function(nameof(Refresh))]
    [AllowAnonymous]
    public async Task<HttpResponseData> Refresh(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/refresh")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<RefreshTokenRequest>(request, cancellationToken);

        var result = await authenticationWorkflow.RefreshAsync(body, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function(nameof(SignOut))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> SignOut(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/sign-out")] HttpRequestData request,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        var currentUser = context.GetCurrentUser();
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthenticatedException();
        }

        var body = await request.ReadFromJsonAsync<SignOutRequest>(cancellationToken)
            ?? SignOutRequest.Empty;

        await authenticationWorkflow.SignOutAsync(
            new SignOutCommand(currentUser.UserId.Value, body.RefreshToken),
            cancellationToken);

        return request.CreateResponse(HttpStatusCode.NoContent);
    }

    private void LimitByIp(HttpRequestData request) =>
        rateLimiter.EnsureAllowed(AnonymousRateLimits.PerIp, AnonymousRateLimits.ClientIpKey(request));

    private static async Task<T> ReadRequiredJsonAsync<T>(HttpRequestData request, CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<T>(cancellationToken);
        if (body is null)
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                ["body"] = ["A request body is required."],
            });
        }

        return body;
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(
        HttpRequestData request,
        HttpStatusCode statusCode,
        T value,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(value, cancellationToken: cancellationToken);
        return response;
    }
}

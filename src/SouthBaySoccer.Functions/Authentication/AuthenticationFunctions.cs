using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Authentication;

public sealed class AuthenticationFunctions(IWhatsAppAuthenticationWorkflow workflow)
{
    [Function(nameof(SignInByPhone))]
    [AllowAnonymous]
    public async Task<HttpResponseData> SignInByPhone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/pickuppal/phone/sign-in")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<SignInByPhoneRequest>(cancellationToken);
        if (body is null)
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                ["body"] = ["A request body is required."],
            });
        }

        var result = await workflow.SignInByPhoneAsync(body, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function(nameof(RequestWhatsAppChallenge))]
    [AllowAnonymous]
    public async Task<HttpResponseData> RequestWhatsAppChallenge(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/whatsapp/challenges")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<RequestWhatsAppChallengeRequest>(cancellationToken);
        if (body is null)
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                ["body"] = ["A request body is required."],
            });
        }

        var result = await workflow.RequestWhatsAppChallengeAsync(body, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.Accepted, result, cancellationToken);
    }

    [Function(nameof(VerifyWhatsAppChallenge))]
    [AllowAnonymous]
    public async Task<HttpResponseData> VerifyWhatsAppChallenge(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/whatsapp/challenges/verify")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<VerifyWhatsAppChallengeRequest>(cancellationToken);
        if (body is null)
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                ["body"] = ["A request body is required."],
            });
        }

        var result = await workflow.VerifyWhatsAppChallengeAsync(body, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function(nameof(Refresh))]
    [AllowAnonymous]
    public async Task<HttpResponseData> Refresh(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/refresh")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<RefreshTokenRequest>(cancellationToken);
        if (body is null)
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                ["body"] = ["A request body is required."],
            });
        }

        var result = await workflow.RefreshAsync(body, cancellationToken);
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

        await workflow.SignOutAsync(
            new SignOutCommand(currentUser.UserId.Value, body.RefreshToken),
            cancellationToken);

        return request.CreateResponse(HttpStatusCode.NoContent);
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

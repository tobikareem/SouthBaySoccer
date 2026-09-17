using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class OnboardingClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.Gone, "expired", OnboardingTokenFailure.Expired)]
    [InlineData(HttpStatusCode.Conflict, "already-registered", OnboardingTokenFailure.AlreadyRegistered)]
    [InlineData(HttpStatusCode.Forbidden, "mismatch", OnboardingTokenFailure.Mismatch)]
    [InlineData(HttpStatusCode.NotFound, "invalid", OnboardingTokenFailure.Invalid)]
    public async Task ValidateRegistration_TokenProblemBehindApiExceptionHandler_MapsByProblemType(
        HttpStatusCode status, string typeSuffix, OnboardingTokenFailure expected)
    {
        var client = Create(status, withApiExceptionHandler: true,
            body: $$"""{"type":"{{OnboardingClient.TokenProblemTypePrefix}}{{typeSuffix}}","title":"Token","status":{{(int)status}}}""");

        var act = () => client.ValidateRegistrationAsync("tok", CancellationToken.None);

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(expected);
    }

    [Fact]
    public async Task ValidateRegistration_BareNotFoundBehindHandler_IsNotATokenFailure()
    {
        var client = Create(HttpStatusCode.NotFound, withApiExceptionHandler: true);

        var act = () => client.ValidateRegistrationAsync("tok", CancellationToken.None);

        await act.Should().ThrowAsync<ApiRequestException>();
    }

    [Fact]
    public async Task ValidateRegistration_TokenFailureWithoutHandler_MapsFromStatusCode()
    {
        var client = Create(HttpStatusCode.Gone, withApiExceptionHandler: false);

        var act = () => client.ValidateRegistrationAsync("tok", CancellationToken.None);

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(OnboardingTokenFailure.Expired);
    }

    [Fact]
    public async Task Register_ValidationProblem_SurfacesAsApiRequestExceptionNotTokenFailure()
    {
        var client = Create(HttpStatusCode.BadRequest, withApiExceptionHandler: true,
            body: """{"title":"Validation failed","errors":{"email":["Invalid email format"]}}""");

        var act = () => client.RegisterAsync(
            new RegisterWithWhatsAppRequest("tok", "Ada", "Okafor", "bad", "secret1", null, "20250708", DateTime.UtcNow),
            CancellationToken.None);

        (await act.Should().ThrowAsync<ApiRequestException>()).Which.Message.Should().Be("Invalid email format");
    }

    [Fact]
    public async Task BeginPhoneSignIn_LegacyTokenBody_IsTreatedAsNoVerificationRequired()
    {
        var client = Create(HttpStatusCode.OK, withApiExceptionHandler: true,
            body: """{"accessToken":"a","refreshToken":"r","accessTokenExpiresAtUtc":"2099-01-01T00:00:00Z"}""");

        var start = await client.BeginPhoneSignInAsync("+15550001234", CancellationToken.None);

        start.VerificationRequired.Should().BeFalse();
        start.Tokens!.AccessToken.Should().Be("a");
    }

    [Fact]
    public async Task BeginPhoneSignIn_VerificationBody_IsParsed()
    {
        var client = Create(HttpStatusCode.Accepted, withApiExceptionHandler: true,
            body: """{"verificationRequired":true,"phoneMasked":"+1 (555) ••• 1234","displayName":"Ada","tokens":null}""");

        var start = await client.BeginPhoneSignInAsync("+15550001234", CancellationToken.None);

        start.VerificationRequired.Should().BeTrue();
        start.DisplayName.Should().Be("Ada");
        start.Tokens.Should().BeNull();
    }

    private static OnboardingClient Create(HttpStatusCode status, bool withApiExceptionHandler, string? body = null)
    {
        HttpMessageHandler inner = new StubHandler(status, body);
        if (withApiExceptionHandler)
        {
            inner = new ApiExceptionHandler { InnerHandler = inner };
        }

        return new OnboardingClient(new HttpClient(inner) { BaseAddress = new Uri("https://example.test/api/") });
    }

    private sealed class StubHandler(HttpStatusCode status, string? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = body is null ? null : new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }
}

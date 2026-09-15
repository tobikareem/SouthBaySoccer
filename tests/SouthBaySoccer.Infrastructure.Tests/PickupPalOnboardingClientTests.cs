using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Authentication.Onboarding;

namespace SouthBaySoccer.Infrastructure.Tests;

public sealed class PickupPalOnboardingClientTests
{
    private const string Token = "8f14e45f-ceea-467a-9a1c-2b3d4e5f6a7b";

    private static readonly PickupPalRegistrationRequest Registration = new(
        Token,
        "Andre",
        "Silva",
        "andre.silva@example.com",
        "pickup-ball-2026",
        "20250708",
        new DateTime(2026, 9, 9, 17, 50, 3, DateTimeKind.Utc));

    [Fact]
    public async Task ValidateRegistrationTokenAsync_WhenApiKeyConfigured_SendsKeyHeaderOnConfiguredRoute()
    {
        HttpRequestMessage? sent = null;
        var client = CreateClient(
            request =>
            {
                sent = request;
                return Json("""{ "valid": true, "phoneNumber": "15550001234" }""");
            },
            options =>
            {
                options.ApiKey = "bot-key";
                options.Routes.RegisterValidate = "v2/register/validate";
            });

        var result = await client.ValidateRegistrationTokenAsync(Token);

        result.Status.Should().Be(RegistrationTokenStatus.Valid);
        result.PhoneNumberDigits.Should().Be("15550001234");
        sent.Should().NotBeNull();
        sent!.Method.Should().Be(HttpMethod.Get);
        sent.RequestUri!.AbsolutePath.Should().Be("/v2/register/validate");
        sent.RequestUri.Query.Should().Be($"?token={Token}");
        sent.Headers.GetValues("X-Api-Key").Should().Equal("bot-key");
    }

    [Fact]
    public async Task ValidateRegistrationTokenAsync_WhenApiKeyMissing_SendsNoKeyHeader()
    {
        HttpRequestMessage? sent = null;
        var client = CreateClient(request =>
        {
            sent = request;
            return Json("""{ "valid": false, "reason": "invalid" }""");
        });

        var result = await client.ValidateRegistrationTokenAsync(Token);

        result.Status.Should().Be(RegistrationTokenStatus.Invalid);
        sent!.Headers.Contains("X-Api-Key").Should().BeFalse();
    }

    [Fact]
    public async Task ValidateRegistrationTokenAsync_WhenReasonExpired_ReturnsExpired()
    {
        var client = CreateClient(_ => Json("""{ "valid": false, "reason": "expired" }"""));

        var result = await client.ValidateRegistrationTokenAsync(Token);

        result.Status.Should().Be(RegistrationTokenStatus.Expired);
        result.PhoneNumberDigits.Should().BeNull();
    }

    [Fact]
    public async Task IsEmailAvailableAsync_WhenLookupReturns404_ReturnsTrueUsingConfiguredRoute()
    {
        HttpRequestMessage? sent = null;
        var client = CreateClient(
            request =>
            {
                sent = request;
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            },
            options => options.Routes.EmailLookup = "lookup/email/{email}");

        var available = await client.IsEmailAvailableAsync("andre+test@example.com");

        available.Should().BeTrue();
        sent!.RequestUri!.AbsolutePath.Should().Be("/lookup/email/andre%2Btest%40example.com");
    }

    [Fact]
    public async Task IsEmailAvailableAsync_WhenLookupReturnsUser_ReturnsFalse()
    {
        var client = CreateClient(_ => Json("""{ "id": "u1", "email": "andre.silva@example.com" }"""));

        var available = await client.IsEmailAvailableAsync("andre.silva@example.com");

        available.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterWithTokenAsync_WhenCreated_PostsBodyAndMapsUser()
    {
        string? body = null;
        HttpRequestMessage? sent = null;
        var client = CreateClient(request =>
        {
            sent = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(
                """
                {
                  "id": "clx8f9a2b0001qwer5678efgh",
                  "email": "andre.silva@example.com",
                  "firstName": "Andre",
                  "lastName": "Silva",
                  "nickName": null,
                  "phoneNumber": "15550001234",
                  "profilePicture": null,
                  "updatedAt": "2026-09-09T17:50:03.204Z"
                }
                """,
                HttpStatusCode.Created);
        });

        var user = await client.RegisterWithTokenAsync(Registration);

        user.Id.Should().Be("clx8f9a2b0001qwer5678efgh");
        user.PhoneNumber.Should().Be("15550001234");
        user.Email.Should().Be("andre.silva@example.com");
        user.PreferredPositions.Should().BeEmpty();
        sent!.Method.Should().Be(HttpMethod.Post);
        sent.RequestUri!.AbsolutePath.Should().Be("/api/users/register/whatsapp");
        body.Should().Contain("\"token\":\"" + Token + "\"")
            .And.Contain("\"password\":\"pickup-ball-2026\"")
            .And.Contain("\"termsVersion\":\"20250708\"")
            .And.Contain("\"termsAcceptedAt\":\"2026-09-09T17:50:03Z\"")
            .And.NotContain("phoneNumber");
    }

    [Theory]
    [InlineData("Invalid registration token", PickupPalOnboardingFailure.TokenInvalid)]
    [InlineData("Registration link has expired", PickupPalOnboardingFailure.TokenExpired)]
    [InlineData("An account with this email already exists", PickupPalOnboardingFailure.EmailAlreadyRegistered)]
    [InlineData("An account with this phone number already exists", PickupPalOnboardingFailure.PhoneAlreadyRegistered)]
    [InlineData("Invalid email format", PickupPalOnboardingFailure.Validation)]
    public async Task RegisterWithTokenAsync_WhenPickupPalReturnsStringErrorShape_ClassifiesFailure(
        string error,
        PickupPalOnboardingFailure expected)
    {
        var client = CreateClient(_ => Json($$"""{ "error": "{{error}}" }""", HttpStatusCode.BadRequest));

        var act = () => client.RegisterWithTokenAsync(Registration);

        var exception = (await act.Should().ThrowAsync<PickupPalOnboardingException>()).Which;
        exception.Failure.Should().Be(expected);
        exception.Detail.Should().Be(error);
    }

    [Fact]
    public async Task RegisterWithTokenAsync_WhenPickupPalReturnsNestedErrorShapeWith500_ThrowsUnavailable()
    {
        var client = CreateClient(_ => Json(
            """{ "error": { "message": "Internal Server Error", "status": 500 } }""",
            HttpStatusCode.InternalServerError));

        var act = () => client.RegisterWithTokenAsync(Registration);

        await act.Should().ThrowAsync<ApplicationServiceUnavailableException>();
    }

    [Fact]
    public async Task RedeemLoginTokenAsync_WhenPickupPalReturnsNestedErrorShapeWith400_ParsesNestedMessage()
    {
        var client = CreateClient(_ => Json(
            """{ "error": { "message": "Login token has expired", "status": 400 } }""",
            HttpStatusCode.BadRequest));

        var act = () => client.RedeemLoginTokenAsync(Token);

        var exception = (await act.Should().ThrowAsync<PickupPalOnboardingException>()).Which;
        exception.Failure.Should().Be(PickupPalOnboardingFailure.TokenExpired);
        exception.Detail.Should().Be("Login token has expired");
    }

    [Fact]
    public async Task RedeemLoginTokenAsync_WhenSucceeds_PostsTokenInBodyOnConfiguredRoute()
    {
        string? body = null;
        HttpRequestMessage? sent = null;
        var client = CreateClient(
            request =>
            {
                sent = request;
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json("""{ "id": "u1", "phoneNumber": "15550001234", "firstName": "Vic" }""");
            },
            options => options.Routes.LoginRedeem = "api/users/login/token");

        var user = await client.RedeemLoginTokenAsync(Token);

        user.Id.Should().Be("u1");
        sent!.RequestUri!.PathAndQuery.Should().Be("/api/users/login/token");
        body.Should().Be($$"""{"token":"{{Token}}"}""");
    }

    [Fact]
    public async Task RedeemLoginTokenAsync_WhenNotFound_ThrowsTokenInvalid()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var act = () => client.RedeemLoginTokenAsync(Token);

        (await act.Should().ThrowAsync<PickupPalOnboardingException>()).Which.Failure.Should().Be(PickupPalOnboardingFailure.TokenInvalid);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DeleteUserAsync_WhenDeletedOrAlreadyGone_Completes(HttpStatusCode statusCode)
    {
        HttpRequestMessage? sent = null;
        var client = CreateClient(
            request =>
            {
                sent = request;
                return new HttpResponseMessage(statusCode);
            },
            options => options.Routes.DeleteUser = "api/users/{id}/delete");

        var act = () => client.DeleteUserAsync("clx8f9a2b0001qwer5678efgh");

        await act.Should().NotThrowAsync();
        sent!.Method.Should().Be(HttpMethod.Delete);
        sent.RequestUri!.AbsolutePath.Should().Be("/api/users/clx8f9a2b0001qwer5678efgh/delete");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task DeleteUserAsync_WhenRefusedOrFailing_ThrowsUnavailableSoOutboxRetries(HttpStatusCode statusCode)
    {
        var client = CreateClient(_ => new HttpResponseMessage(statusCode));

        var act = () => client.DeleteUserAsync("u1");

        await act.Should().ThrowAsync<ApplicationServiceUnavailableException>();
    }

    [Fact]
    public async Task RegisterWithTokenAsync_WhenNetworkFails_ThrowsUnavailable()
    {
        var client = CreateClient(_ => throw new HttpRequestException("connection refused"));

        var act = () => client.RegisterWithTokenAsync(Registration);

        await act.Should().ThrowAsync<ApplicationServiceUnavailableException>();
    }

    [Fact]
    public void PickupPalOnboardingClient_WhenConstructed_TakesNoLoggerSoRequestUrisAreNeverLogged()
    {
        // The registration token and the email travel in request URIs under the Pickup Pal
        // contract; the URI-logging ban is enforced structurally: no ILogger can reach this type.
        var constructorParameters = typeof(PickupPalOnboardingClient)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        constructorParameters.Should().NotContain(type =>
            typeof(ILogger).IsAssignableFrom(type) || typeof(ILoggerFactory).IsAssignableFrom(type));
        typeof(PickupPalOnboardingClient).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Should().NotContain("Microsoft.Extensions.Logging");
    }

    private static PickupPalOnboardingClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> send,
        Action<PickupPalApiOptions>? configure = null)
    {
        var options = new PickupPalApiOptions { BaseUrl = "https://pickuppal.test" };
        configure?.Invoke(options);
        return new PickupPalOnboardingClient(new HttpClient(new StubHttpMessageHandler(send)), Options.Create(options));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}

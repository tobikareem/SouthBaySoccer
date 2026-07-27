using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Infrastructure.Authentication;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

public sealed class JwtTokenServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 06, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IssueAccessToken_WhenPrincipalIsValid_ReturnsValidatableTokenWithUtcExpiryAndActiveKey()
    {
        var userId = Guid.NewGuid();
        var service = CreateService(NowUtc, activeKeyId: "key-2", keys: [Key("key-1", "old-secret-material-1234567890"), Key("key-2", "new-secret-material-1234567890")]);

        var issued = service.IssueAccessToken(new AccessTokenIssueRequest(
            userId,
            ["Player", "Admin", "Player", " "],
            ["CanManageSessions", "CanManageSessions"]));
        var validated = service.ValidateAccessToken(issued.Token);

        issued.Token.Should().NotBeNullOrWhiteSpace();
        issued.KeyId.Should().Be("key-2");
        issued.ExpiresAtUtc.Should().Be(NowUtc.AddMinutes(15));
        issued.ExpiresAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        validated.IsValid.Should().BeTrue();
        validated.UserId.Should().Be(userId);
        validated.Roles.Should().BeEquivalentTo(["Admin", "Player"], options => options.WithStrictOrdering());
        validated.Policies.Should().BeEquivalentTo(["CanManageSessions"]);
        validated.KeyId.Should().Be("key-2");
        validated.ExpiresAtUtc.Should().Be(issued.ExpiresAtUtc);
    }

    [Fact]
    public void ValidateAccessToken_WhenSignedByRetiredOverlapKey_ReturnsValidPrincipal()
    {
        var userId = Guid.NewGuid();
        var oldKey = Key("key-1", "old-secret-material-1234567890");
        var newKey = Key("key-2", "new-secret-material-1234567890");
        var issuingService = CreateService(NowUtc, activeKeyId: oldKey.KeyId, keys: [oldKey]);
        var validatingService = CreateService(NowUtc.AddMinutes(1), activeKeyId: newKey.KeyId, keys: [oldKey, newKey]);
        var issued = issuingService.IssueAccessToken(new AccessTokenIssueRequest(userId, ["Player"], []));

        var validated = validatingService.ValidateAccessToken(issued.Token);

        validated.IsValid.Should().BeTrue();
        validated.UserId.Should().Be(userId);
        validated.KeyId.Should().Be(oldKey.KeyId);
        validated.Roles.Should().BeEquivalentTo(["Player"]);
    }

    [Fact]
    public void ValidateAccessToken_WhenTokenIsExpired_ReturnsExpiredFailure()
    {
        var service = CreateService(NowUtc);
        var issued = service.IssueAccessToken(new AccessTokenIssueRequest(Guid.NewGuid(), ["Player"], []));
        var validatingService = CreateService(NowUtc.AddMinutes(16));

        var validated = validatingService.ValidateAccessToken(issued.Token);

        validated.IsValid.Should().BeFalse();
        validated.FailureCode.Should().Be("expired_token");
        validated.UserId.Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_WhenSignatureIsTampered_ReturnsInvalidSignatureFailure()
    {
        var service = CreateService(NowUtc);
        var issued = service.IssueAccessToken(new AccessTokenIssueRequest(Guid.NewGuid(), ["Player"], []));
        var tampered = ReplaceLastCharacter(issued.Token);

        var validated = service.ValidateAccessToken(tampered);

        validated.IsValid.Should().BeFalse();
        validated.FailureCode.Should().Be("invalid_signature");
    }

    [Fact]
    public void ValidateAccessToken_WhenIssuerDoesNotMatch_ReturnsInvalidIssuerFailure()
    {
        var key = Key("key-1", "shared-secret-material-1234567890");
        var issuingService = CreateService(NowUtc, issuer: "southbay-soccer-auth", keys: [key]);
        var validatingService = CreateService(NowUtc, issuer: "different-issuer", keys: [key]);
        var issued = issuingService.IssueAccessToken(new AccessTokenIssueRequest(Guid.NewGuid(), ["Player"], []));

        var validated = validatingService.ValidateAccessToken(issued.Token);

        validated.IsValid.Should().BeFalse();
        validated.FailureCode.Should().Be("invalid_issuer");
    }

    [Fact]
    public void Constructor_WhenActiveKeyIsMissingFromValidationKeys_Throws()
    {
        Action act = () => CreateService(NowUtc, activeKeyId: "missing-key", keys: [Key("key-1", "secret-material-1234567890")]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*active JWT signing key id must be present*");
    }

    [Fact]
    public async Task IssueAndValidateAccessToken_WhenCalledConcurrentlyOnSharedInstance_AllTokensValidate()
    {
        var service = CreateService(NowUtc);
        var userId = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            var issued = service.IssueAccessToken(new AccessTokenIssueRequest(userId, ["Player"], ["CanManageSessions"]));
            return service.ValidateAccessToken(issued.Token);
        })));

        results.Should().OnlyContain(result => result.IsValid && result.UserId == userId);
    }

    private static JwtTokenService CreateService(
        DateTime nowUtc,
        string issuer = "southbay-soccer-auth",
        string audience = "southbay-soccer-api",
        string activeKeyId = "key-1",
        IReadOnlyList<JwtSigningKeyOptions>? keys = null)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(nowUtc);

        return new JwtTokenService(
            Options.Create(new JwtTokenOptions
            {
                Issuer = issuer,
                Audience = audience,
                AccessTokenLifetime = TimeSpan.FromMinutes(15),
                ActiveKeyId = activeKeyId,
                SigningKeys = (keys ?? [Key("key-1", "secret-material-1234567890")]).ToList(),
            }),
            clock.Object);
    }

    private static JwtSigningKeyOptions Key(string keyId, string secret) =>
        new()
        {
            KeyId = keyId,
            Secret = secret,
        };

    private static string ReplaceLastCharacter(string token)
    {
        var replacement = token[^1] == 'A' ? 'B' : 'A';
        return token[..^1] + replacement;
    }
}


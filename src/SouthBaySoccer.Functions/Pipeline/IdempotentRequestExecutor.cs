using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Idempotency;

namespace SouthBaySoccer.Functions.Pipeline;

public sealed class IdempotentRequestExecutor(
    ICurrentUser currentUser,
    IClock clock,
    IIdempotencyStore idempotencyStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<HttpResponseData> ExecuteAsync<TResponse>(
        HttpRequestData request,
        string operationName,
        string idempotencyKey,
        object requestBody,
        Func<CancellationToken, Task<IdempotentResponse<TResponse>>> operation,
        CancellationToken cancellationToken = default,
        Func<TResponse, IReadOnlyDictionary<string, string>>? responseHeaderFactory = null)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["A non-empty Idempotency-Key header of 160 characters or fewer is required."],
            });
        }

        var identityUserId = currentUser.UserId;
        var requestHash = ComputeHash(JsonSerializer.Serialize(requestBody, JsonOptions));
        var existing = await idempotencyStore.FindAsync(identityUserId, operationName, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new ApplicationConflictException("The Idempotency-Key was already used with a different request.");
            }

            if (existing.CompletedAtUtc is null || existing.ResponseStatusCode is null || existing.ResponseBodyJson is null)
            {
                throw new ApplicationConflictException("The idempotent request is still processing.");
            }

            return await WriteStoredJsonAsync(
                request,
                (HttpStatusCode)existing.ResponseStatusCode.Value,
                existing.ResponseBodyJson,
                responseHeaderFactory is null
                    ? null
                    : responseHeaderFactory(JsonSerializer.Deserialize<TResponse>(existing.ResponseBodyJson, JsonOptions)
                        ?? throw new InvalidOperationException("The stored idempotent response could not be read.")),
                cancellationToken);
        }

        var record = await idempotencyStore.CreateAsync(
            identityUserId,
            playerProfileId: null,
            operationName,
            idempotencyKey,
            requestHash,
            clock.UtcNow.AddHours(24),
            cancellationToken);

        IdempotentResponse<TResponse> response;
        try
        {
            response = await operation(cancellationToken);
        }
        catch
        {
            await idempotencyStore.AbandonAsync(record.Id, clock.UtcNow.AddSeconds(-1), CancellationToken.None);
            throw;
        }

        var responseJson = JsonSerializer.Serialize(response.Body, JsonOptions);
        await idempotencyStore.CompleteAsync(
            record.Id,
            (int)response.StatusCode,
            responseJson,
            ComputeHash(responseJson),
            clock.UtcNow,
            cancellationToken);

        return await WriteStoredJsonAsync(
            request,
            response.StatusCode,
            responseJson,
            responseHeaderFactory?.Invoke(response.Body),
            cancellationToken);
    }

    private static async Task<HttpResponseData> WriteStoredJsonAsync(
        HttpRequestData request,
        HttpStatusCode statusCode,
        string responseJson,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        if (headers is not null)
        {
            foreach (var header in headers)
            {
                response.Headers.Add(header.Key, header.Value);
            }
        }
        await response.WriteStringAsync(responseJson, cancellationToken);
        return response;
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}

public sealed record IdempotentResponse<TResponse>(HttpStatusCode StatusCode, TResponse Body);

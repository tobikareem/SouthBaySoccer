using System.Collections.Concurrent;
using System.Reflection;

namespace SouthBaySoccer.Functions.Pipeline;

public sealed class ReflectionEndpointPolicyResolver : IEndpointPolicyResolver
{
    // Entry points are a fixed compile-time set, so successful resolutions are cached for the
    // process lifetime. Failures are never cached: a misclassified endpoint keeps throwing and
    // stays fail-closed on every request.
    private readonly ConcurrentDictionary<string, EndpointAccessRequirement> resolvedEntryPoints =
        new(StringComparer.Ordinal);

    public EndpointAccessRequirement Resolve(string entryPoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        return resolvedEntryPoints.GetOrAdd(entryPoint, ResolveEntryPoint);
    }

    private static EndpointAccessRequirement ResolveEntryPoint(string entryPoint)
    {
        var separatorIndex = entryPoint.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == entryPoint.Length - 1)
        {
            throw new EndpointClassificationException($"Function entry point '{entryPoint}' is not a fully qualified method name.");
        }

        var typeName = entryPoint[..separatorIndex];
        var methodName = entryPoint[(separatorIndex + 1)..];
        var type = Type.GetType(typeName)
            ?? AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(typeName, throwOnError: false))
                .FirstOrDefault(candidate => candidate is not null)
            ?? throw new EndpointClassificationException($"Function entry point type '{typeName}' could not be resolved.");

        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new EndpointClassificationException($"Function entry point method '{entryPoint}' could not be resolved.");

        return Resolve(type, method);
    }

    private static EndpointAccessRequirement Resolve(MemberInfo type, MemberInfo method)
    {
        var anonymous = method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true)
            ?? type.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true);
        var policy = method.GetCustomAttribute<RequirePolicyAttribute>(inherit: true)
            ?? type.GetCustomAttribute<RequirePolicyAttribute>(inherit: true);

        if (anonymous is not null && policy is not null)
        {
            throw new EndpointClassificationException("Function endpoint declares both anonymous and policy access.");
        }

        if (anonymous is not null)
        {
            return EndpointAccessRequirement.Anonymous;
        }

        if (policy is not null)
        {
            if (string.IsNullOrWhiteSpace(policy.Policy))
            {
                throw new EndpointClassificationException("Function endpoint declares an empty authorization policy.");
            }

            return EndpointAccessRequirement.RequirePolicy(policy.Policy);
        }

        throw new EndpointClassificationException("Function endpoint must declare either AllowAnonymous or RequirePolicy.");
    }
}

using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Functions.Authentication;

namespace SouthBaySoccer.Functions.Pipeline;

public static class FunctionsApplicationBuilderExtensions
{
    public static FunctionsApplicationBuilder AddSouthBaySoccerHttpPipeline(this FunctionsApplicationBuilder builder)
    {
        builder.Services.AddScoped<FunctionCurrentUser>();
        builder.Services.AddScoped<ICurrentUser>(services => services.GetRequiredService<FunctionCurrentUser>());
        builder.Services.AddScoped<IFunctionCurrentUserAccessor>(services => services.GetRequiredService<FunctionCurrentUser>());
        builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
        builder.Services.AddSingleton<ICorrelationIdProvider, CorrelationIdProvider>();
        builder.Services.AddSingleton<IProblemDetailsMapper, ProblemDetailsMapper>();
        builder.Services.AddSingleton<IEndpointPolicyResolver, ReflectionEndpointPolicyResolver>();
        builder.Services.AddScoped<IEndpointAuthorizer, EndpointAuthorizer>();
        builder.Services.AddScoped<CorrelationMiddleware>();
        builder.Services.AddScoped<ExceptionHandlingMiddleware>();
        builder.Services.AddScoped<AuthenticationMiddleware>();
        builder.Services.AddScoped<AuthorizationMiddleware>();

        builder.UsePipelineMiddleware<CorrelationMiddleware>();
        builder.UsePipelineMiddleware<ExceptionHandlingMiddleware>();
        builder.UsePipelineMiddleware<AuthenticationMiddleware>();
        builder.UsePipelineMiddleware<AuthorizationMiddleware>();

        return builder;
    }

    private static FunctionsApplicationBuilder UsePipelineMiddleware<TMiddleware>(this FunctionsApplicationBuilder builder)
        where TMiddleware : IFunctionsWorkerMiddleware
    {
        builder.Use(next => context =>
        {
            var middleware = context.InstanceServices.GetRequiredService<TMiddleware>();
            return middleware.Invoke(context, next);
        });

        return builder;
    }
}


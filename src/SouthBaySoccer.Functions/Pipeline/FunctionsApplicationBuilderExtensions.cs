using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Payments;
using SouthBaySoccer.Application.Features.Players;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Application.Features.Stats;
using SouthBaySoccer.Application.Features.Waivers;
using FluentValidation;
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
        builder.Services.AddScoped<IValidator<RequestWhatsAppChallengeCommand>, RequestWhatsAppChallengeCommandValidator>();
        builder.Services.AddScoped<IValidator<VerifyWhatsAppChallengeCommand>, VerifyWhatsAppChallengeCommandValidator>();
        builder.Services.AddScoped<RequestWhatsAppChallengeCommandHandler>();
        builder.Services.AddScoped<VerifyWhatsAppChallengeCommandHandler>();
        builder.Services.AddScoped<IWhatsAppAuthenticationWorkflow, WhatsAppAuthenticationWorkflow>();
        builder.Services.AddScoped<IValidator<UpdateMyProfileCommand>, UpdateMyProfileCommandValidator>();
        builder.Services.AddScoped<IValidator<CreateGuestProfileCommand>, CreateGuestProfileCommandValidator>();
        builder.Services.AddScoped<IValidator<CreateProfileMergeCommand>, CreateProfileMergeCommandValidator>();
        builder.Services.AddScoped<GetMyProfileQueryHandler>();
        builder.Services.AddScoped<UpdateMyProfileCommandHandler>();
        builder.Services.AddScoped<CreateGuestProfileCommandHandler>();
        builder.Services.AddScoped<CreateProfileMergeCommandHandler>();
        builder.Services.AddScoped<GetCurrentWaiverQueryHandler>();
        builder.Services.AddScoped<AcceptCurrentWaiverCommandHandler>();
        builder.Services.AddScoped<GetMyWaiverEligibilityQueryHandler>();
        builder.Services.AddScoped<IValidator<CreateSeasonCommand>, CreateSeasonCommandValidator>();
        builder.Services.AddScoped<IValidator<CreateVenueCommand>, CreateVenueCommandValidator>();
        builder.Services.AddScoped<IValidator<CreateSessionCommand>, CreateSessionCommandValidator>();
        builder.Services.AddScoped<IValidator<CreateRecurrenceRuleCommand>, CreateRecurrenceRuleCommandValidator>();
        builder.Services.AddScoped<IValidator<CreateSessionOccurrenceCommand>, CreateSessionOccurrenceCommandValidator>();
        builder.Services.AddScoped<CreateSeasonCommandHandler>();
        builder.Services.AddScoped<ListSeasonsQueryHandler>();
        builder.Services.AddScoped<CreateVenueCommandHandler>();
        builder.Services.AddScoped<ListVenuesQueryHandler>();
        builder.Services.AddScoped<CreateSessionCommandHandler>();
        builder.Services.AddScoped<ListUpcomingSessionsQueryHandler>();
        builder.Services.AddScoped<CancelSessionCommandHandler>();
        builder.Services.AddScoped<CreateRecurrenceRuleCommandHandler>();
        builder.Services.AddScoped<CreateSessionOccurrenceCommandHandler>();        builder.Services.AddScoped<IPlayerSessionEligibilityService, PlayerSessionEligibilityService>();
        builder.Services.AddScoped<IPaymentEligibilityService, DeferredPaymentEligibilityService>();
        builder.Services.AddScoped<IValidator<SubmitRsvpCommand>, SubmitRsvpCommandValidator>();
        builder.Services.AddScoped<IValidator<AdminOverrideRsvpCommand>, AdminOverrideRsvpCommandValidator>();
        builder.Services.AddScoped<IValidator<CheckInPlayerCommand>, CheckInPlayerCommandValidator>();
        builder.Services.AddScoped<SubmitRsvpCommandHandler>();
        builder.Services.AddScoped<CancelRsvpCommandHandler>();
        builder.Services.AddScoped<GetMyRsvpQueryHandler>();
        builder.Services.AddScoped<AdminOverrideRsvpCommandHandler>();
        builder.Services.AddScoped<CheckInPlayerCommandHandler>();
        builder.Services.AddScoped<RecordNoShowsCommandHandler>();
        builder.Services.AddScoped<IValidator<CreateMatchCommand>, CreateMatchCommandValidator>();
        builder.Services.AddScoped<IValidator<RecordMatchEventsCommand>, RecordMatchEventsCommandValidator>();
        builder.Services.AddScoped<IValidator<RecordMatchResultsCommand>, RecordMatchResultsCommandValidator>();
        builder.Services.AddScoped<IValidator<SubmitPeerFeedbackCommand>, SubmitPeerFeedbackCommandValidator>();
        builder.Services.AddScoped<IValidator<ReviewMatchEventCommand>, ReviewMatchEventCommandValidator>();
        builder.Services.AddScoped<IValidator<ResolveMatchReviewCommand>, ResolveMatchReviewCommandValidator>();
        builder.Services.AddScoped<IValidator<AddStatCorrectionCommand>, AddStatCorrectionCommandValidator>();
        builder.Services.AddScoped<IValidator<ReassignProfileStatsCommand>, ReassignProfileStatsCommandValidator>();
        builder.Services.AddScoped<IValidator<LockMatchStatsCommand>, LockMatchStatsCommandValidator>();
        builder.Services.AddScoped<IValidator<GetSeasonLeaderboardQuery>, GetSeasonLeaderboardQueryValidator>();
        builder.Services.AddScoped<IValidator<GetPlayerStatsQuery>, GetPlayerStatsQueryValidator>();
        builder.Services.AddScoped<IValidator<GetPlayerRecentFormQuery>, GetPlayerRecentFormQueryValidator>();
        builder.Services.AddScoped<CreateMatchCommandHandler>();
        builder.Services.AddScoped<RecordMatchEventsCommandHandler>();
        builder.Services.AddScoped<RecordMatchResultsCommandHandler>();
        builder.Services.AddScoped<SubmitPeerFeedbackCommandHandler>();
        builder.Services.AddScoped<ReviewMatchEventCommandHandler>();
        builder.Services.AddScoped<ResolveMatchReviewCommandHandler>();
        builder.Services.AddScoped<AddStatCorrectionCommandHandler>();
        builder.Services.AddScoped<ReassignProfileStatsCommandHandler>();
        builder.Services.AddScoped<LockMatchStatsCommandHandler>();
        builder.Services.AddScoped<GetSeasonLeaderboardQueryHandler>();
        builder.Services.AddScoped<GetPlayerStatsQueryHandler>();
        builder.Services.AddScoped<GetMyPlayerStatsQueryHandler>();
        builder.Services.AddScoped<GetPlayerRecentFormQueryHandler>();
        builder.Services.AddScoped<FunctionCurrentUser>();
        builder.Services.AddScoped<ICurrentUser>(services => services.GetRequiredService<FunctionCurrentUser>());
        builder.Services.AddScoped<IFunctionCurrentUserAccessor>(services => services.GetRequiredService<FunctionCurrentUser>());
        builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
        builder.Services.AddSingleton<ICorrelationIdProvider, CorrelationIdProvider>();
        builder.Services.AddSingleton<IProblemDetailsMapper, ProblemDetailsMapper>();
        builder.Services.AddSingleton<IEndpointPolicyResolver, ReflectionEndpointPolicyResolver>();
        builder.Services.AddScoped<IEndpointAuthorizer, EndpointAuthorizer>();
        builder.Services.AddScoped<IdempotentRequestExecutor>();
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

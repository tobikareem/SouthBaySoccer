using FluentValidation;

namespace SouthBaySoccer.Application.Features.Announcements;

public sealed class PostAnnouncementCommandValidator : AbstractValidator<PostAnnouncementCommand>
{
    /// <summary>The composer's character budget; the client counter and this rule must agree.</summary>
    public const int MaxBodyLength = 500;

    public PostAnnouncementCommandValidator()
    {
        RuleFor(x => x.GroupChatId).NotEmpty();
        // Measured after trimming, because that is the text that gets stored: trailing whitespace
        // should not be able to push an otherwise-valid message over the limit.
        RuleFor(x => x.Body)
            .NotEmpty()
            .Must(body => body is null || body.Trim().Length <= MaxBodyLength)
            .WithMessage($"'Body' must be {MaxBodyLength} characters or fewer.");
    }
}

public sealed class GetGroupAnnouncementsQueryValidator : AbstractValidator<GetGroupAnnouncementsQuery>
{
    /// <summary>Upper bound on a feed page, mirroring the paging cap used elsewhere in the API.</summary>
    public const int MaxLimit = 50;

    public GetGroupAnnouncementsQueryValidator()
    {
        RuleFor(x => x.GroupChatId).NotEmpty();
        RuleFor(x => x.Limit).InclusiveBetween(1, MaxLimit);
    }
}

public sealed class GetSentAnnouncementsQueryValidator : AbstractValidator<GetSentAnnouncementsQuery>
{
    /// <summary>Upper bound on the admin's "Recently sent" list.</summary>
    public const int MaxLimit = 25;

    public GetSentAnnouncementsQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, MaxLimit);
    }
}

public sealed class MarkGroupAnnouncementsReadCommandValidator : AbstractValidator<MarkGroupAnnouncementsReadCommand>
{
    public MarkGroupAnnouncementsReadCommandValidator()
    {
        RuleFor(x => x.GroupChatId).NotEmpty();
    }
}

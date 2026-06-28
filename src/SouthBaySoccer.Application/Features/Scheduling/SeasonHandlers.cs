using FluentValidation;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed class CreateSeasonCommandHandler(
    IValidator<CreateSeasonCommand> validator,
    ISeasonRepository seasonRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<SeasonModel> HandleAsync(CreateSeasonCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var existing = await seasonRepository.FindByNameAsync(command.Name.Trim(), cancellationToken);
        if (existing is not null)
        {
            throw new ApplicationConflictException("Season already exists.");
        }

        var season = new Season
        {
            Id = Guid.NewGuid(),
            Name = command.Name.Trim(),
            StartsAtUtc = command.StartsAtUtc,
            EndsAtUtc = command.EndsAtUtc,
        };

        await seasonRepository.AddAsync(season, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SchedulingMappers.ToModel(season);
    }
}

public sealed class ListSeasonsQueryHandler(ISeasonRepository seasonRepository)
{
    public async Task<IReadOnlyList<SeasonModel>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var seasons = await seasonRepository.ListActiveAsync(cancellationToken);
        return seasons.Select(SchedulingMappers.ToModel).ToArray();
    }
}

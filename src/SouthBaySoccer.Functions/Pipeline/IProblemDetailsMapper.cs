using Microsoft.AspNetCore.Mvc;

namespace SouthBaySoccer.Functions.Pipeline;

public interface IProblemDetailsMapper
{
    ProblemDetails Map(Exception exception, string correlationId);
}

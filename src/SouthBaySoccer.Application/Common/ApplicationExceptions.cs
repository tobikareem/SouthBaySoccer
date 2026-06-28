namespace SouthBaySoccer.Application.Common;

/// <summary>
/// Base exception for expected application-flow failures.
/// </summary>
public abstract class ApplicationExceptionBase : Exception
{
    protected ApplicationExceptionBase(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Indicates that the current request is not authenticated.
/// </summary>
public sealed class ApplicationUnauthenticatedException : ApplicationExceptionBase
{
    public ApplicationUnauthenticatedException()
        : base("Authentication is required.")
    {
    }
}

/// <summary>
/// Indicates that an expected resource was not found.
/// </summary>
public sealed class ApplicationNotFoundException : ApplicationExceptionBase
{
    public ApplicationNotFoundException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Indicates that the request conflicts with current application state.
/// </summary>
public sealed class ApplicationConflictException : ApplicationExceptionBase
{
    public ApplicationConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Indicates that the current user is authenticated but not authorized for the requested action.
/// </summary>
public sealed class ApplicationForbiddenException : ApplicationExceptionBase
{
    public ApplicationForbiddenException(string message)
        : base(message)
    {
    }
}

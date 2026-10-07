namespace SouthBaySoccer.Domain.Enumerations;

/// <summary>Kinds of successful initial authentication.</summary>
public enum UserActivityType
{
    /// <summary>An existing account signed in.</summary>
    SignIn,
    /// <summary>In-app registration completed and issued a session.</summary>
    SignUp,
}

namespace SouthBaySoccer.Functions.Pipeline;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequirePolicyAttribute(string policy) : Attribute
{
    public string Policy { get; } = policy;
}

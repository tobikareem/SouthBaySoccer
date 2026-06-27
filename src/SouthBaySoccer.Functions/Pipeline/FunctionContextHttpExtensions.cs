using Microsoft.Azure.Functions.Worker;

namespace SouthBaySoccer.Functions.Pipeline;

internal static class FunctionContextHttpExtensions
{
    public static bool IsHttpTrigger(this FunctionContext context) =>
        context.FunctionDefinition.InputBindings.Values.Any(binding =>
            string.Equals(binding.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase));
}

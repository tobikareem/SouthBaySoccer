using FluentAssertions;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class StartupMigrationGuardTests
{
    [Fact]
    public async Task Program_WhenFunctionHostStarts_DoesNotApplyDatabaseMigrations()
    {
        var programPath = FindProgramFile();
        var source = await File.ReadAllTextAsync(programPath);

        source.Should().NotContain(".Migrate(");
        source.Should().NotContain(".MigrateAsync(");
        source.Should().NotContain("EnsureCreated(");
        source.Should().NotContain("EnsureCreatedAsync(");
        source.Should().NotContain("dotnet ef database update");
    }

    private static string FindProgramFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "SouthBaySoccer.Functions", "Program.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate src/SouthBaySoccer.Functions/Program.cs from the test output directory.");
    }
}
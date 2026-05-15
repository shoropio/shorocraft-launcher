using Xunit;

namespace ShoroCraftLauncher.Tests;

public sealed class ManualIntegrationFactAttribute : FactAttribute
{
    public ManualIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHOROCRAFT_RUN_INTEGRATION_TESTS") != "1")
            Skip = "Manual integration test. Set SHOROCRAFT_RUN_INTEGRATION_TESTS=1 to run.";
    }
}

using SXA.RTX.Analytics.Application.Abstractions;

namespace SXA.RTX.Analytics.Application.Tests;

public sealed class ApplicationSmokeTests
{
    [Fact]
    public void ConfigurationItemDto_Should_Hold_Values()
    {
        var dto = new ConfigurationItemDto(Guid.NewGuid(), "Key1", "Val1", "desc", "General", DateTime.UtcNow);
        Assert.Equal("Key1", dto.Key);
        Assert.Equal("General", dto.Category);
    }

    [Fact]
    public void IConfigurationService_Should_Be_Interface()
    {
        Assert.True(typeof(IConfigurationService).IsInterface);
    }
}

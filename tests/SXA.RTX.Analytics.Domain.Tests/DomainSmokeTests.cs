using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Domain.Enums;

namespace SXA.RTX.Analytics.Domain.Tests;

public sealed class DomainSmokeTests
{
    [Fact]
    public void ApplicationSetting_Should_Have_Unique_Key()
    {
        var a = new ApplicationSetting { Key = "Theme", Value = "Dark", Category = "UI" };
        var b = new ApplicationSetting { Key = "Theme", Value = "Light" };
        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void AuditLog_Should_Require_Action_And_Entity()
    {
        var log = new AuditLog { Action = "Create", EntityName = "DataSource", EntityId = Guid.NewGuid().ToString() };
        Assert.Equal("Create", log.Action);
        Assert.NotEqual(Guid.Empty, log.Id);
    }

    [Fact]
    public void DataSourceType_Should_Have_SqlServer_And_Odbc()
    {
        Assert.True(Enum.IsDefined(typeof(DataSourceType), DataSourceType.SqlServer));
        Assert.True(Enum.IsDefined(typeof(DataSourceType), DataSourceType.Odbc));
    }

    [Fact]
    public void BaseEntity_Should_AutoGenerate_Id_And_Timestamp()
    {
        var setting = new ApplicationSetting { Key = "k", Value = "v" };
        Assert.NotEqual(Guid.Empty, setting.Id);
        Assert.True((DateTime.UtcNow - setting.CreatedAtUtc).TotalSeconds < 5);
    }
}

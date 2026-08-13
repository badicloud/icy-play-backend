namespace IcyPlay.IntegrationTests;

public sealed class ApiFoundationTests
{
    [Fact]
    public void ApiAssembly_ShouldBeLoadable()
    {
        var assembly = typeof(Program).Assembly;

        Assert.Equal("IcyPlay.Api", assembly.GetName().Name);
    }

    [Fact]
    public void SystemController_ShouldBeRegisteredInApiAssembly()
    {
        var controllerType = typeof(Program).Assembly.GetType("IcyPlay.Api.Controllers.SystemController");

        Assert.NotNull(controllerType);
    }
}

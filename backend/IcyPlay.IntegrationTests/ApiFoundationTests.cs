using System.Reflection;
using FluentAssertions.Execution;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace IcyPlay.IntegrationTests;

public sealed class ApiFoundationTests
{
    /// <summary>
    /// Authorization attributes add up. An action cannot loosen a role its
    /// controller demands, only <c>[AllowAnonymous]</c> can — so a role over
    /// the whole of the bookings controller silently refused every account
    /// that is not a customer, and the page read the refusal as a breakage.
    /// </summary>
    [Fact]
    public void BookingsController_ShouldAskOnlyThatAReaderIsSignedIn()
    {
        var controller = typeof(Program).Assembly
            .GetType("IcyPlay.Api.Controllers.BookingsController")!;

        var onController = controller.GetCustomAttributes<AuthorizeAttribute>(true).ToList();
        var onMine = controller.GetMethod("Mine")!
            .GetCustomAttributes<AuthorizeAttribute>(true).ToList();
        var onCreate = controller.GetMethod("Create")!
            .GetCustomAttributes<AuthorizeAttribute>(true).ToList();

        using (new AssertionScope())
        {
            // Signed in, and nothing more: reading your own record is not a
            // privilege of the role that happens to make most of the records.
            onController.Should().ContainSingle().Which.Roles.Should().BeNullOrEmpty();
            onMine.Should().BeEmpty();
            // Booking a court is still a customer's to do.
            onCreate.Should().ContainSingle().Which.Roles.Should().Be(UserRoleName.Customer);
        }
    }

    [Fact]
    public void ApiAssembly_WhenReferencedByTestHost_ShouldBeLoadable()
    {
        var assembly = typeof(Program).Assembly;

        Assert.Equal("IcyPlay.Api", assembly.GetName().Name);
    }

    [Fact]
    public void ApiAssembly_WhenScanned_ShouldContainSystemController()
    {
        var controllerType = typeof(Program).Assembly.GetType("IcyPlay.Api.Controllers.SystemController");

        Assert.NotNull(controllerType);
    }
}

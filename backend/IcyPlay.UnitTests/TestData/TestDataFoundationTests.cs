using FluentAssertions.Execution;

namespace IcyPlay.UnitTests.TestData;

public sealed class TestDataFoundationTests
{
    [Fact]
    public void Should_Return_Deterministic_Id_When_Scope_And_Sequence_Match()
    {
        // Arrange
        const string scope = "customer";
        const int sequence = 2;

        // Act
        var firstId = TestIds.For(scope, sequence);
        var secondId = TestIds.For(scope, sequence);

        // Assert
        using (new AssertionScope())
        {
            firstId.Should().Be(secondId);
            firstId.Should().NotBe(Guid.Empty);
        }
    }

    [Fact]
    public void Should_Return_Fresh_Users_When_Builder_Is_Called_Twice()
    {
        // Arrange
        var builder = new UserBuilder();

        // Act
        var firstUser = builder.Build();
        var secondUser = builder.Build();

        // Assert
        firstUser.Should().NotBeSameAs(secondUser);
    }
}

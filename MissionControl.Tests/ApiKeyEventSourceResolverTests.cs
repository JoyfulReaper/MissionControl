using Microsoft.Extensions.Options;
using MissionControl.Gateway.Security;
using Xunit;

namespace MissionControl.Tests;

public sealed class ApiKeyEventSourceResolverTests
{
    [Fact]
    public void CorrectKeyResolvesExpectedSource()
    {
        var resolver = CreateResolver();

        Assert.True(resolver.TryResolve("alpha-api-key-32-characters-long", out var source));
        Assert.Equal("alpha", source);
    }

    [Fact]
    public void WrongKeyIsRejected()
    {
        Assert.False(CreateResolver().TryResolve("wrong-api-key-32-characters-long", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyAndNullKeysAreRejected(string? apiKey)
    {
        Assert.False(CreateResolver().TryResolve(apiKey, out _));
    }

    [Fact]
    public void KeysAreCaseSensitive()
    {
        Assert.False(
            CreateResolver().TryResolve(
                "ALPHA-API-KEY-32-CHARACTERS-LONG",
                out _));
    }

    [Fact]
    public void EqualLengthDifferentKeysDoNotResolveIncorrectly()
    {
        Assert.False(
            CreateResolver().TryResolve(
                "alpha-api-key-32-characters-xong",
                out _));
    }

    [Fact]
    public void MultipleConfiguredSourcesResolveIndependently()
    {
        var resolver = CreateResolver();

        Assert.True(resolver.TryResolve("alpha-api-key-32-characters-long", out var alpha));
        Assert.True(resolver.TryResolve("bravo-api-key-32-characters-long", out var bravo));
        Assert.Equal("alpha", alpha);
        Assert.Equal("bravo", bravo);
    }

    private static ApiKeyEventSourceResolver CreateResolver()
    {
        return new ApiKeyEventSourceResolver(
            Options.Create(
                new EventSourceOptions
                {
                    Sources =
                    [
                        new EventSourceRegistration
                        {
                            Name = "alpha",
                            ApiKey = "alpha-api-key-32-characters-long"
                        },
                        new EventSourceRegistration
                        {
                            Name = "bravo",
                            ApiKey = "bravo-api-key-32-characters-long"
                        }
                    ]
                }));
    }
}

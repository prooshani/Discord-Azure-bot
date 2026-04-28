using discord_chatbot.Services;

namespace discord_chatbot.Tests;

public sealed class MessageParserTests
{
    private readonly MessageParser _parser = new();

    [Fact]
    public void Parse_ShouldExtractDistinctWorkItemsAndPullRequests()
    {
        var result = _parser.Parse("Need #5210, #5210 and !6040 plus !6040");

        Assert.Equal(new[] { 5210 }, result.WorkItemIds.OrderBy(x => x));
        Assert.Equal(new[] { 6040 }, result.PullRequestIds.OrderBy(x => x));
    }

    [Fact]
    public void Parse_ShouldHandleMultipleReferences()
    {
        var result = _parser.Parse("Refs: #1 #2 !3 !4");

        Assert.Equal(new[] { 1, 2 }, result.WorkItemIds.OrderBy(x => x));
        Assert.Equal(new[] { 3, 4 }, result.PullRequestIds.OrderBy(x => x));
    }

    [Fact]
    public void Parse_ShouldIgnoreEmptyMessage()
    {
        var result = _parser.Parse(string.Empty);

        Assert.False(result.HasReferences);
        Assert.Empty(result.WorkItemIds);
        Assert.Empty(result.PullRequestIds);
    }
}

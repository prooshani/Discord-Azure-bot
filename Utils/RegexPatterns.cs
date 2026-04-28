using System.Text.RegularExpressions;

namespace discord_chatbot.Utils;

public static class RegexPatterns
{
    public static readonly Regex WorkItemPattern = new(@"(?<![\w<])#(\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly Regex PullRequestPattern = new(@"(?<![\w<])!(\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

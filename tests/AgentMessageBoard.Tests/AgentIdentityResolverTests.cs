using System.Security.Claims;
using AgentMessageBoard.Server.Auth;
using AgentMessageBoard.Server.Domain;
using ModelContextProtocol.Protocol;

namespace AgentMessageBoard.Tests;

public sealed class AgentIdentityResolverTests
{
    [Fact]
    public void Describes_the_agent_from_mcp_client_info_name_and_version()
    {
        Assert.Equal("claude-code/2.1.231", AgentIdentityResolver.DescribeAgent(new Implementation { Name = "claude-code", Version = "2.1.231" }, "curl/8.0"));
    }

    [Fact]
    public void Uses_the_client_name_alone_when_version_is_blank()
    {
        Assert.Equal("codex", AgentIdentityResolver.DescribeAgent(new Implementation { Name = "codex", Version = "" }, "curl/8.0"));
        Assert.Equal("codex", AgentIdentityResolver.DescribeAgent(new Implementation { Name = "codex", Version = "   " }, "curl/8.0"));
    }

    [Fact]
    public void Falls_back_to_the_trimmed_http_user_agent_header()
    {
        Assert.Equal("curl/8.0", AgentIdentityResolver.DescribeAgent(null, "  curl/8.0 "));
        Assert.Equal("curl/8.0", AgentIdentityResolver.DescribeAgent(new Implementation { Name = "", Version = "1" }, "curl/8.0"));
    }

    [Fact]
    public void Reports_unknown_when_nothing_identifies_the_agent()
    {
        Assert.Equal(AgentIdentity.Unknown, AgentIdentityResolver.DescribeAgent(null, null));
        Assert.Equal(AgentIdentity.Unknown, AgentIdentityResolver.DescribeAgent(null, "   "));
    }

    [Fact]
    public void Resolves_user_from_sub_and_preferred_username()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "abc-123"), new Claim("preferred_username", "alice")], "Bearer"));

        var identity = AgentIdentityResolver.Resolve(user, new Implementation { Name = "test-agent", Version = "1.2.3" }, null);

        Assert.Equal(new AgentIdentity("abc-123", "alice", "test-agent/1.2.3"), identity);
    }

    [Fact]
    public void Falls_back_to_the_subject_when_there_is_no_username()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "abc-123")], "Bearer"));

        var identity = AgentIdentityResolver.Resolve(user, null, "curl/8.0");

        Assert.Equal("abc-123", identity.UserId);
        Assert.Equal("abc-123", identity.UserName);
        Assert.Equal("curl/8.0", identity.UserAgent);
    }

    [Fact]
    public void Anonymous_principal_resolves_to_anonymous()
    {
        var identity = AgentIdentityResolver.Resolve(null, null, null);

        Assert.Equal(new AgentIdentity("anonymous", "anonymous", "unknown"), identity);
    }
}

using System.Text.Json.Nodes;
using Prism.Automation;
using Xunit;

namespace Prism.UnitTests.Assistant;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/assistant/README.md">AI assistant specification.</seealso>
[Trait("Feature", "Assistant")]
[Trait("Layer", "Unit")]
public class AssistantCliSetupTests
{
    static readonly (string command, string[] args) Bridge = ("dotnet", ["/app/mcp/Prism.Mcp.dll"]);

    [Fact(DisplayName = "AST-U008 | A fresh codex exec call is JSONL, read-only and carries the bridge config; no --sandbox flag")]
    [Trait("TestId", "AST-U008")]
    public void FreshArgsCarryBridgeWithoutSandboxFlag()
    {
        var a = AssistantService.BuildCodexArgs(Bridge, allowFiles: true, sessionId: null, model: "gpt-5.3-codex", effort: "high", imageFiles: ["/tmp/a.png"]);
        Assert.Equal("exec", a[0]);
        Assert.DoesNotContain("resume", a);
        Assert.Contains("--json", a);
        // the read-only sandbox must travel as a -c override: `exec resume` has no --sandbox flag
        Assert.DoesNotContain("--sandbox", a);
        Assert.Contains(a, x => x == "sandbox_mode=\"read-only\"");
        Assert.Contains(a, x => x.StartsWith("mcp_servers.prism.command=") && x.Contains("dotnet"));
        Assert.Contains(a, x => x.StartsWith("mcp_servers.prism.args=") && x.Contains("Prism.Mcp.dll"));
        Assert.Contains(a, x => x == "mcp_servers.prism.disabled_tools=[\"assistant\"]");
        Assert.Equal(new[] { "-m", "gpt-5.3-codex" }, a.SkipWhile(x => x != "-m").Take(2).ToArray());
        Assert.Contains(a, x => x == "model_reasoning_effort=high");
        Assert.Equal(new[] { "--image", "/tmp/a.png" }, a.SkipWhile(x => x != "--image").Take(2).ToArray());
        Assert.Equal("-", a[^1]);
    }

    [Fact(DisplayName = "AST-U009 | A resumed call keeps the session id, still avoids --sandbox, and re-passes bridge and image flags")]
    [Trait("TestId", "AST-U009")]
    public void ResumeArgsKeepSessionWithoutSandboxFlag()
    {
        var a = AssistantService.BuildCodexArgs(Bridge, allowFiles: true, sessionId: "1111-2222", model: "", effort: "", imageFiles: null);
        Assert.Equal(new[] { "exec", "resume", "1111-2222" }, a.Take(3).ToArray());
        Assert.DoesNotContain("--sandbox", a);
        Assert.Contains(a, x => x == "sandbox_mode=\"read-only\"");
        Assert.Contains(a, x => x.StartsWith("mcp_servers.prism.command="));
        Assert.DoesNotContain(a, x => x == "-m");
    }

    [Fact(DisplayName = "AST-U010 | With file access off the bridge args gain the restricted discovery file and run_script/file stay disabled")]
    [Trait("TestId", "AST-U010")]
    public void RestrictedArgsPassDiscoveryAndDisabledTools()
    {
        var a = AssistantService.BuildCodexArgs(Bridge, allowFiles: false, sessionId: null, model: null, effort: null, imageFiles: null);
        var mcpArgs = a.Single(x => x.StartsWith("mcp_servers.prism.args="));
        Assert.Contains("--discovery", mcpArgs);
        Assert.Contains("assistant-automation.json", mcpArgs);
        Assert.Contains(a, x => x == "mcp_servers.prism.disabled_tools=[\"assistant\", \"file\", \"run_script\"]");
    }

    [Fact(DisplayName = "AST-U011 | Claude config merge adds mcpServers.prism and keeps other servers and keys; malformed JSON throws")]
    [Trait("TestId", "AST-U011")]
    public void MergeClaudeJsonKeepsExisting()
    {
        var merged = AssistantService.MergeClaudeJson("""{ "mcpServers": { "other": { "command": "x" } }, "theme": "dark" }""", "dotnet", ["/mcp/Prism.Mcp.dll"]);
        var root = JsonNode.Parse(merged)!;
        Assert.Equal("x", root["mcpServers"]!["other"]!["command"]!.ToString());
        Assert.Equal("dark", root["theme"]!.ToString());
        Assert.Equal("dotnet", root["mcpServers"]!["prism"]!["command"]!.ToString());
        Assert.Equal("/mcp/Prism.Mcp.dll", root["mcpServers"]!["prism"]!["args"]![0]!.ToString());
        var fresh = JsonNode.Parse(AssistantService.MergeClaudeJson(null, "/opt/Prism.Mcp", []))!;
        Assert.Equal("/opt/Prism.Mcp", fresh["mcpServers"]!["prism"]!["command"]!.ToString());
        Assert.ThrowsAny<Exception>(() => AssistantService.MergeClaudeJson("not json", "c", []));
    }

    [Fact(DisplayName = "AST-U012 | Codex TOML merge appends when missing and replaces only the prism table when present")]
    [Trait("TestId", "AST-U012")]
    public void MergeCodexTomlAppendAndReplace()
    {
        var added = AssistantService.MergeCodexToml("model = \"gpt-5\"\n\n[mcp_servers.other]\ncommand = \"x\"\n", "/opt/Prism.Mcp", []);
        Assert.Contains("model = \"gpt-5\"", added);
        Assert.Contains("[mcp_servers.other]\ncommand = \"x\"", added);
        Assert.Contains("[mcp_servers.prism]\ncommand = \"/opt/Prism.Mcp\"\nargs = []", added);
        var replaced = AssistantService.MergeCodexToml("[mcp_servers.prism]\ncommand = \"old\"\n\n[mcp_servers.other]\ncommand = \"x\"\n", "/new/Prism.Mcp", ["--discovery", "/tmp/a.json"]);
        Assert.Equal(1, replaced.Split("[mcp_servers.prism]").Length - 1);
        Assert.Contains("command = \"/new/Prism.Mcp\"", replaced);
        Assert.Contains("args = [\"--discovery\", \"/tmp/a.json\"]", replaced);
        Assert.Contains("[mcp_servers.other]\ncommand = \"x\"", replaced);
        Assert.DoesNotContain("old", replaced);
    }

    [Theory(DisplayName = "AST-U013 | ModelIdOf strips the picker's '(label)' decoration; plain ids pass through")]
    [Trait("TestId", "AST-U013")]
    [InlineData("claude-haiku-5-5   (Claude Haiku 5.5)", "claude-haiku-5-5")]
    [InlineData("gpt-6.1-sol   (GPT-6.1-Sol)", "gpt-6.1-sol")]
    [InlineData("claude-sonnet-5-5", "claude-sonnet-5-5")]
    [InlineData("", "")]
    public void ModelIdStripsLabel(string display, string id) => Assert.Equal(id, AssistantService.ModelIdOf(display));

    [Fact(DisplayName = "AST-U014 | UsageText reports input and output tokens, adding cached only when present")]
    [Trait("TestId", "AST-U014")]
    public void UsageTextFormat()
    {
        Assert.StartsWith("usage: ", AssistantService.UsageText(1234, 56));
        Assert.Contains("in", AssistantService.UsageText(1234, 56));
        Assert.Contains("out", AssistantService.UsageText(1234, 56));
        Assert.Contains("cached", AssistantService.UsageText(10, 5, 200));
        Assert.DoesNotContain("cached", AssistantService.UsageText(10, 5));
    }
}

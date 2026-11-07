using System.Text.Json.Nodes;
using Prism.Automation;
using Xunit;

namespace Prism.UnitTests.Assistant;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/assistant/README.md">AI assistant specification.</seealso>
[Trait("Feature", "Assistant")]
[Trait("Layer", "Unit")]
public class AssistantCatalogTests
{
    [Fact(DisplayName = "AST-U001 | Codex model catalog parses slug, name and supported reasoning levels, skipping hidden models")]
    [Trait("TestId", "AST-U001")]
    public void ParseCodexModelsReadsCatalog()
    {
        var json = """
            { "models": [
              { "slug": "gpt-5.3-codex", "display_name": "GPT-5.3 Codex", "visibility": "list",
                "supported_reasoning_levels": [ { "effort": "low" }, { "effort": "medium" }, { "effort": "high" }, { "effort": "xhigh" } ] },
              { "slug": "gpt-5.3-codex-mini", "visibility": "list", "supported_reasoning_levels": [ { "effort": "medium" }, { "effort": "high" } ] },
              { "slug": "internal-only", "visibility": "hide", "supported_reasoning_levels": [] }
            ] }
            """;
        var models = AssistantService.ParseCodexModels(json);
        Assert.Equal(2, models.Count);
        Assert.Equal("gpt-5.3-codex", models[0].Id);
        Assert.Equal("GPT-5.3 Codex", models[0].Label);
        Assert.Equal(new[] { "low", "medium", "high", "xhigh" }, models[0].Efforts);
        Assert.Equal(new[] { "medium", "high" }, models[1].Efforts);
    }

    [Fact(DisplayName = "AST-U002 | Codex catalog tolerates a bare array, empty and malformed input")]
    [Trait("TestId", "AST-U002")]
    public void ParseCodexModelsToleratesShapes()
    {
        var arr = AssistantService.ParseCodexModels("[ { \"slug\": \"m1\" } ]");
        Assert.Single(arr); Assert.Equal("m1", arr[0].Id); Assert.Empty(arr[0].Efforts);
        Assert.Empty(AssistantService.ParseCodexModels(""));
        Assert.Empty(AssistantService.ParseCodexModels("not json"));
        Assert.Empty(AssistantService.ParseCodexModels("{}"));
    }

    [Fact(DisplayName = "AST-U003 | Anthropic catalog keeps claude ids and offers the known effort set")]
    [Trait("TestId", "AST-U003")]
    public void ParseAnthropicModelsFilters()
    {
        var root = JsonNode.Parse("{ \"data\": [ { \"id\": \"claude-sonnet-5-5\", \"display_name\": \"Claude Sonnet\" }, { \"id\": \"other-model\" } ] }");
        var models = AssistantService.ParseAnthropicModels(root);
        Assert.Single(models);
        Assert.Equal("claude-sonnet-5-5", models[0].Id);
        Assert.Equal(new[] { "low", "medium", "high" }, models[0].Efforts);
        Assert.Empty(AssistantService.ParseAnthropicModels(JsonNode.Parse("{}")));
    }

    [Fact(DisplayName = "AST-U004 | OpenAI catalog keeps tool-using chat models and effort only on reasoning models")]
    [Trait("TestId", "AST-U004")]
    public void ParseOpenAiModelsFilters()
    {
        var root = JsonNode.Parse(
            "{ \"data\": [ { \"id\": \"gpt-5.2\" }, { \"id\": \"gpt-4o\" }, { \"id\": \"o3\" }," +
            " { \"id\": \"whisper-1\" }, { \"id\": \"dall-e-3\" }, { \"id\": \"text-embedding-3-large\" }, { \"id\": \"gpt-image-1\" } ] }");
        var models = AssistantService.ParseOpenAiModels(root);
        Assert.Equal(new[] { "gpt-4o", "gpt-5.2", "o3" }, models.Select(m => m.Id).ToArray());
        Assert.Empty(models.First(m => m.Id == "gpt-4o").Efforts);
        Assert.Equal(new[] { "minimal", "low", "medium", "high" }, models.First(m => m.Id == "gpt-5.2").Efforts);
        Assert.Equal(new[] { "minimal", "low", "medium", "high" }, models.First(m => m.Id == "o3").Efforts);
    }

    [Theory(DisplayName = "AST-U005 | Chat-model and reasoning-model classifiers agree with the picker offer")]
    [Trait("TestId", "AST-U005")]
    [InlineData("gpt-4o", true, false)]
    [InlineData("gpt-5.3-codex", true, true)]
    [InlineData("o4-mini", true, true)]
    [InlineData("chatgpt-4o-latest", true, false)]
    [InlineData("whisper-1", false, false)]
    [InlineData("dall-e-3", false, false)]
    [InlineData("text-embedding-3-large", false, false)]
    [InlineData("gpt-image-1", false, false)]
    [InlineData("codex-mini-latest", true, true)]
    public void ModelClassifiers(string id, bool chat, bool reasoning)
    {
        Assert.Equal(chat, AssistantService.IsChatModel(id));
        Assert.Equal(reasoning, AssistantService.IsReasoningModel(id));
    }

    [Theory(DisplayName = "AST-U006 | The restricted gate blocks file tools and write actions but keeps read-only tools")]
    [Trait("TestId", "AST-U006")]
    [InlineData("file", "{}", true)]
    [InlineData("mesh_export", "{}", true)]
    [InlineData("drawing_export", "{}", true)]
    [InlineData("script_editor", "{ \"action\": \"run\" }", true)]
    [InlineData("script_editor", "{ \"action\": \"save\" }", true)]
    [InlineData("script_editor", "{ \"action\": \"set\", \"text\": \"x\" }", false)]
    [InlineData("script_editor", "{ \"action\": \"rollback\" }", false)]
    [InlineData("render_control", "{ \"action\": \"save\" }", true)]
    [InlineData("render_control", "{ \"action\": \"status\" }", false)]
    [InlineData("render_setup", "{ \"action\": \"set_background\", \"path\": \"a.png\" }", true)]
    [InlineData("render_setup", "{ \"action\": \"set\", \"background\": \"white\" }", false)]
    [InlineData("screenshot", "{}", false)]
    [InlineData("box", "{}", false)]
    public void RestrictedGate(string tool, string argsJson, bool expected)
        => Assert.Equal(expected, AutomationApi.Restricted(tool, JsonNode.Parse(argsJson)!.AsObject()));

    [Fact(DisplayName = "AST-U007 | Restricted inspection follows calls nested inside batch")]
    [Trait("TestId", "AST-U007")]
    public void RestrictedGateFollowsBatch()
    {
        var safe = JsonNode.Parse("{ \"calls\": [ { \"tool\": \"box\" }, { \"tool\": \"screenshot\" } ] }")!.AsObject();
        Assert.False(AutomationApi.Restricted("batch", safe));
        var write = JsonNode.Parse("{ \"calls\": [ { \"tool\": \"script_editor\", \"args\": { \"action\": \"run\" } } ] }")!.AsObject();
        Assert.True(AutomationApi.Restricted("batch", write));
    }
}

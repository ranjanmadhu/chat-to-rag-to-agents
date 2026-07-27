using Chatbot.Application.Chat;
using Chatbot.Application.Tools.Deterministic;

namespace Chatbot.Tests;

public sealed class DeterministicChatToolServiceTests
{
    private readonly DeterministicChatToolService service = new(
        [
            new CurrentDateTool(),
            new CurrentTimeTool(),
            new CalculatorTool()
        ]);

    [Fact]
    public async Task TryExecuteAsync_Requires_Enabled_Tool()
    {
        var result = await service.TryExecuteAsync("what time is it", ["calculator"], CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryExecuteAsync_Returns_Time_When_Time_Tool_Enabled()
    {
        var result = await service.TryExecuteAsync("what time is it", ["time"], CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("time", result.ToolId);
        Assert.Contains("Current UTC time:", result.Output);
    }

    [Fact]
    public async Task TryExecuteAsync_Returns_Date_When_Date_Tool_Enabled()
    {
        var result = await service.TryExecuteAsync("what is the date today?", ["date"], CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("date", result.ToolId);
        Assert.Contains("Current UTC date:", result.Output);
    }

    [Fact]
    public async Task TryExecuteAsync_Evaluates_Calculator_When_Enabled()
    {
        var result = await service.TryExecuteAsync("calculate 12 * (3 + 4)", ["calculator"], CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("calculator", result.ToolId);
        Assert.Equal("Result: 84", result.Output);
    }

    [Fact]
    public async Task TryExecuteAsync_Evaluates_Calculator_Word_Operators()
    {
        var result = await service.TryExecuteAsync("what is 25 times 25?", ["calculator"], CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("calculator", result.ToolId);
        Assert.Equal("Result: 625", result.Output);
    }
}

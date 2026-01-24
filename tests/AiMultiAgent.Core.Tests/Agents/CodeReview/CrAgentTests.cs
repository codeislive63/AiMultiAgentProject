using AiMultiAgent.Core.Agents.CodeReview;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AiMultiAgent.Core.Tests.Agents.CodeReview;

public sealed class CrAgentTests_Working
{
    private const string FileName = "TestFile.cs";
    private const string FileContent = "class Test { }";

    [Fact]
    public async Task ReviewAsync_ReturnsValidResult_WhenLLMReturnsValidJson()
    {
        // Arrange
        var chatMock = new Mock<IChatClient>();
        chatMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken ct) =>
            {
                // Ответ LLM с валидным JSON
                var llmMessage = new ChatMessage(ChatRole.Assistant, """
                {
                  "summary": "All good",
                  "issues": [],
                  "suggestions": []
                }
                """);
                return new ChatResponse(new[] { llmMessage });
            });

        var agent = new CodeReviewerAgent(chatMock.Object, NullLogger<CodeReviewerAgent>.Instance);

        // Act
        var result = await agent.ReviewAsync(FileName, FileContent);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("All good", result.Summary);
        Assert.Empty(result.Issues);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public async Task ReviewAsync_RepairsInvalidJson_WhenLLMReturnsBadJsonFirst()
    {
        // Arrange
        var chatMock = new Mock<IChatClient>();
        var callCount = 0;

        chatMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken ct) =>
            {
                callCount++;

                if (callCount == 1)
                {
                    // Первый вызов — некорректный JSON
                    var firstMessage = new ChatMessage(ChatRole.Assistant, "INVALID_JSON");
                    return new ChatResponse(new[] { firstMessage });
                }
                else
                {
                    // Второй вызов — исправленный JSON
                    var secondMessage = new ChatMessage(ChatRole.Assistant, """
                    {
                      "summary": "Repaired",
                      "issues": [],
                      "suggestions": []
                    }
                    """);
                    return new ChatResponse(new[] { secondMessage });
                }
            });

        var agent = new CodeReviewerAgent(chatMock.Object, NullLogger<CodeReviewerAgent>.Instance);

        // Act
        var result = await agent.ReviewAsync(FileName, FileContent);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Repaired", result.Summary);
        Assert.Empty(result.Issues);
        Assert.Empty(result.Suggestions);
        Assert.Equal(2, callCount); // проверяем повторный вызов
    }

    [Fact]
    public async Task ReviewAsync_Throws_WhenLLMReturnsUnrepairableJson()
    {
        // Arrange
        var chatMock = new Mock<IChatClient>();
        chatMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken ct) =>
            {
                // LLM возвращает полностью некорректный JSON
                var msg = new ChatMessage(ChatRole.Assistant, "NOT_JSON");
                return new ChatResponse(new[] { msg });
            });

        var agent = new CodeReviewerAgent(chatMock.Object, NullLogger<CodeReviewerAgent>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.ReviewAsync(FileName, FileContent)
        );
    }
}

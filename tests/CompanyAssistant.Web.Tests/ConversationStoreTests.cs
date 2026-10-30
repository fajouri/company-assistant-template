using CompanyAssistant.Web.Chat;

namespace CompanyAssistant.Web.Tests;

public sealed class ConversationStoreTests
{
    private static ChatSettings Settings(int historyTurns) => new(
        "https://chat.invalid", "fictional-key", "test", "2024-02-01", 1, 1000, historyTurns, 2000, 512, 30, 10);

    [Fact]
    public async Task Concurrent_completions_keep_question_answer_and_sources_paired_when_evicting()
    {
        var store = new ConversationStore(Settings(4));
        var conversation = store.Create("tenant", "owner");
        await Task.WhenAll(Enumerable.Range(1, 100).Select(index => Task.Run(() =>
            store.AppendExchange(conversation, $"question-{index}", $"answer-{index}",
                [new(index, $"doc-{index}", "Policy", "1", "hash")]))));
        var turns = conversation.Turns;
        Assert.Equal(8, turns.Count);
        for (var i = 0; i < turns.Count; i += 2)
        {
            var question = turns[i];
            var answer = turns[i + 1];
            var id = Assert.Single(answer.Sources!).ChunkId;
            Assert.Equal("user", question.Role);
            Assert.Equal("assistant", answer.Role);
            Assert.Equal($"question-{id}", question.Text);
            Assert.Equal($"answer-{id}", answer.Text);
        }
        Assert.Null(store.FindOwned(conversation.Id, "other-tenant", "owner"));
        Assert.Null(store.FindOwned(conversation.Id, "tenant", "other-owner"));
    }

    [Fact]
    public void Store_refuses_unbounded_or_empty_provenance_instead_of_truncating_it()
    {
        var store = new ConversationStore(Settings(1));
        var conversation = store.Create("tenant", "owner");
        Assert.Throws<ArgumentException>(() => store.AppendExchange(conversation, "question", "answer", []));
        Assert.Throws<ArgumentException>(() => store.AppendExchange(conversation, "question", "answer",
            Enumerable.Range(1, 3).Select(index => new SourceStamp(index, "doc", "Policy", "1", "hash")).ToArray()));
        Assert.Empty(conversation.Turns);
    }
}

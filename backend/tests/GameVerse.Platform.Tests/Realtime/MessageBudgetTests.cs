using GameVerse.Platform.Realtime;
using Microsoft.Extensions.Time.Testing;

namespace GameVerse.Platform.Tests.Realtime;

public class MessageBudgetTests
{
    [Fact]
    public void A_burst_up_to_the_rate_passes_and_the_next_message_is_refused()
    {
        var clock = new FakeTimeProvider();
        var budget = new MessageBudget(3, clock);

        Enumerable.Range(0, 3).Select(_ => budget.TrySpend()).ShouldAllBe(ok => ok);
        budget.TrySpend().ShouldBeFalse();
    }

    [Fact]
    public void The_budget_refills_at_the_rate_and_never_beyond_it()
    {
        var clock = new FakeTimeProvider();
        var budget = new MessageBudget(4, clock);
        while (budget.TrySpend())
        {
        }

        clock.Advance(TimeSpan.FromMilliseconds(250)); // a quarter second at 4 per second: one message
        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(5)); // a long quiet spell still allows only one burst
        Enumerable.Range(0, 4).Select(_ => budget.TrySpend()).ShouldAllBe(ok => ok);
        budget.TrySpend().ShouldBeFalse();
    }
}

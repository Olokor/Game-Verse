namespace GameVerse.Platform.Realtime;

/// <summary>
/// A token bucket for one connection's messages (CLAUDE.md section 7). Holds up to <c>perSecond</c> tokens and refills
/// at that rate, so short bursts pass and a sustained flood is refused. Wall clock: it guards the server, not the game.
/// </summary>
internal sealed class MessageBudget(int perSecond, TimeProvider clock)
{
    private readonly double capacity = Math.Max(1, perSecond);
    private double tokens = Math.Max(1, perSecond);
    private long last = clock.GetTimestamp();

    /// <summary>Spends one token. False when the bucket is empty: drop the message.</summary>
    public bool TrySpend()
    {
        var now = clock.GetTimestamp();
        tokens = Math.Min(capacity, tokens + (clock.GetElapsedTime(last, now).TotalSeconds * capacity));
        last = now;
        if (tokens < 1)
        {
            return false;
        }

        tokens -= 1;
        return true;
    }
}

namespace GameVerse.Platform.Matchmaking;

/// <summary>A Glicko-2 rating on the familiar Glicko scale (1500 is a new player).</summary>
public readonly record struct GlickoRating(double Rating, double Deviation, double Volatility)
{
    public static GlickoRating Default => new(Glicko2.DefaultRating, Glicko2.DefaultDeviation, Glicko2.DefaultVolatility);
}

/// <summary>One game in a rating period: the opponent's rating before it, and the score (1 win, 0.5 draw, 0 loss).</summary>
public readonly record struct GlickoGame(GlickoRating Opponent, double Score);

/// <summary>
/// Glicko-2, exactly as in Mark Glickman, "Example of the Glicko-2 system" (2013), steps 1 to 8, with the Illinois
/// algorithm for the volatility (step 5). Server-side rating state only: never replayed, so doubles are fine here (the
/// determinism rules of CLAUDE.md section 3 apply to GameVerse.Rules).
/// </summary>
public static class Glicko2
{
    public const double DefaultRating = 1500;
    public const double DefaultDeviation = 350;
    public const double DefaultVolatility = 0.06;

    /// <summary>The system constant: how much volatility may change. Glickman suggests 0.3 to 1.2.</summary>
    public const double Tau = 0.5;

    /// <summary>Glicko to Glicko-2 scale factor (400 / ln 10).</summary>
    public const double Scale = 173.7178;

    private const double Epsilon = 0.000001;

    /// <summary>The rating after one rating period with these games (no games: only the deviation grows).</summary>
    public static GlickoRating Update(GlickoRating player, IReadOnlyList<GlickoGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        // Step 2: to the Glicko-2 scale.
        var mu = (player.Rating - DefaultRating) / Scale;
        var phi = player.Deviation / Scale;
        var sigma = player.Volatility;

        if (games.Count == 0)
        {
            // Step 6 alone: an idle period only adds uncertainty.
            return new GlickoRating(player.Rating, Math.Min(DefaultDeviation, Math.Sqrt((phi * phi) + (sigma * sigma)) * Scale), sigma);
        }

        // Steps 3 and 4: estimated variance v and improvement delta.
        double vInverse = 0, sum = 0;
        foreach (var game in games)
        {
            var muJ = (game.Opponent.Rating - DefaultRating) / Scale;
            var gJ = G(game.Opponent.Deviation / Scale);
            var e = E(mu, muJ, gJ);
            vInverse += gJ * gJ * e * (1 - e);
            sum += gJ * (game.Score - e);
        }

        var v = 1 / vInverse;
        var delta = v * sum;

        // Step 5: new volatility.
        var newSigma = Volatility(phi, sigma, v, delta);

        // Steps 6 and 7: new deviation and rating.
        var phiStar = Math.Sqrt((phi * phi) + (newSigma * newSigma));
        var newPhi = 1 / Math.Sqrt((1 / (phiStar * phiStar)) + (1 / v));
        var newMu = mu + (newPhi * newPhi * sum);

        // Step 8: back to the Glicko scale.
        return new GlickoRating((newMu * Scale) + DefaultRating, Math.Min(DefaultDeviation, newPhi * Scale), newSigma);
    }

    /// <summary>The rating after <paramref name="periods"/> periods without games: the deviation drifts up, capped at 350.</summary>
    public static GlickoRating Idle(GlickoRating player, int periods)
    {
        var rating = player;
        for (var i = 0; i < periods && rating.Deviation < DefaultDeviation; i++)
        {
            rating = Update(rating, []);
        }

        return rating;
    }

    /// <summary>
    /// A multiplayer result as head-to-heads (owner decision 2026-11-12): against each other player, a better place scores
    /// 1, a worse place 0, the same place 0.5. Everyone is rated from everyone's ratings before the game.
    /// </summary>
    public static IReadOnlyList<GlickoRating> RateFinishingOrder(IReadOnlyList<GlickoRating> before, IReadOnlyList<int> places)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(places);
        if (before.Count != places.Count)
        {
            throw new ArgumentException("One place per player.", nameof(places));
        }

        var after = new GlickoRating[before.Count];
        for (var i = 0; i < before.Count; i++)
        {
            var games = new List<GlickoGame>(before.Count - 1);
            for (var j = 0; j < before.Count; j++)
            {
                if (i != j)
                {
                    games.Add(new GlickoGame(before[j], places[i] < places[j] ? 1 : places[i] > places[j] ? 0 : 0.5));
                }
            }

            after[i] = Update(before[i], games);
        }

        return after;
    }

    private static double G(double phi) => 1 / Math.Sqrt(1 + (3 * phi * phi / (Math.PI * Math.PI)));

    private static double E(double mu, double muJ, double gJ) => 1 / (1 + Math.Exp(-gJ * (mu - muJ)));

    private static double Volatility(double phi, double sigma, double v, double delta)
    {
        var a = Math.Log(sigma * sigma);
        double F(double x)
        {
            var ex = Math.Exp(x);
            var denominator = (phi * phi) + v + ex;
            return (ex * ((delta * delta) - (phi * phi) - v - ex) / (2 * denominator * denominator)) - ((x - a) / (Tau * Tau));
        }

        var bigA = a;
        double bigB;
        if (delta * delta > (phi * phi) + v)
        {
            bigB = Math.Log((delta * delta) - (phi * phi) - v);
        }
        else
        {
            var k = 1;
            while (F(a - (k * Tau)) < 0)
            {
                k++;
            }

            bigB = a - (k * Tau);
        }

        var fA = F(bigA);
        var fB = F(bigB);
        while (Math.Abs(bigB - bigA) > Epsilon)
        {
            var c = bigA + ((bigA - bigB) * fA / (fB - fA));
            var fC = F(c);
            if (fC * fB <= 0)
            {
                bigA = bigB;
                fA = fB;
            }
            else
            {
                fA /= 2;
            }

            bigB = c;
            fB = fC;
        }

        return Math.Exp(bigA / 2);
    }
}

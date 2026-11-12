using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Session;

namespace GameVerse.Core
{
    /// <summary>
    /// What every game implements in Unity (CLAUDE.md 4.2). A game never handles auth, matchmaking, rewards or
    /// leaderboards: it receives a <see cref="SessionManifest"/>, presents state, and reports through <see cref="IGameHost"/>.
    /// Rules are never reimplemented here; games call the shared GameVerse.Rules engine.
    /// </summary>
    public interface IGameModule
    {
        /// <summary>Catalog id, e.g. "ludo". Matches <see cref="SessionManifest.GameId"/>.</summary>
        string GameId { get; }

        /// <summary>Load assets and build the scene for this session. Emit "ready" through the host when done.</summary>
        UniTask LoadAsync(SessionManifest manifest, IGameHost host);

        /// <summary>Present an authoritative state from the server (or the local engine in offline modes).</summary>
        void ApplyServerState(GameStateEnvelope envelope);

        /// <summary>Release everything the game loaded.</summary>
        UniTask UnloadAsync();
    }
}

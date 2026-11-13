#nullable enable
using GameVerse.Games.Ludo;
using UnityEngine;

namespace GameVerse.Platform.Shell
{
    /// <summary>Paces development autoplay to about a person's speed, so the server's message budget is never hit.</summary>
    public static class Autoplay
    {
        private static float next;

        public static void Step(LudoModule module)
        {
            if (Time.realtimeSinceStartup < next)
            {
                return;
            }

            next = Time.realtimeSinceStartup + 0.35f;
            module.AutoplayStep();
        }
    }
}

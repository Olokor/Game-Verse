#nullable enable
using GameVerse.Games.Ludo.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Tests
{
    public class EffectsTests
    {
        [Test]
        public void Busy_moments_never_spawn_more_than_the_particle_cap()
        {
            var layer = new VisualElement();
            var effects = new Effects(layer, cell => cell * 20, () => 20);

            effects.Confetti(Color.red);
            effects.Burst(new Vector2(3, 3), Color.green);
            effects.Ripple(new Vector2(7.5f, 7.5f), Color.blue);

            Assert.That(effects.Live, Is.EqualTo(Effects.Cap), "confetti fills the budget; the burst and ripple are refused");
            Assert.That(layer.childCount, Is.EqualTo(Effects.Cap));
        }
    }
}

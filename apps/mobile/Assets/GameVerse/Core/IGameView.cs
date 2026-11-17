using UnityEngine.UIElements;

namespace GameVerse.Core
{
    /// <summary>
    /// A game whose presentation is UI Toolkit (board and card games). The platform's game screen puts <see cref="View"/>
    /// inside its HUD frame after <see cref="IGameModule.LoadAsync"/>. Games that render a world (3D, physics) do not
    /// implement this; they draw with their own camera under the same HUD.
    /// </summary>
    public interface IGameView
    {
        VisualElement View { get; }
    }
}

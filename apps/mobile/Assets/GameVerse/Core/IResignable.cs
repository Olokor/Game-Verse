namespace GameVerse.Core
{
    /// <summary>A game the HUD can resign from (the platform's Leave button, after a confirm).</summary>
    public interface IResignable
    {
        void Resign();
    }
}

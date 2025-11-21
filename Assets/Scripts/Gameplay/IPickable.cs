namespace Gameplay
{
    /// <summary>
    /// Interface for molecule visualizers that can respond to pickup state changes.
    /// Allows PickableMolecule to work with any visualizer implementation.
    /// </summary>
    public interface IPickable
    {
        /// <summary>
        /// Sets whether the molecule is in "picked up" state (faster animation, more glow)
        /// </summary>
        /// <param name="pickedUp">True if molecule is picked up, false otherwise</param>
        void SetPickedUpState(bool pickedUp);
    }
}

using StardewModdingAPI;

namespace FishingHorizonsExpanded.Framework.Rods
{
    /// <summary>Harmony patches for the custom rods' special effects.</summary>
    /// <remarks>
    /// Previously held the golden rod's "always gold quality" patch.
    /// Now empty — the feeder rod's multi-fish mechanic is handled entirely by
    /// <see cref="Tackle.DoubleHookPatches"/>, which detects the feeder rod in the
    /// BobberBar constructor postfix. This file is kept as a placeholder for future
    /// rod-specific patches (e.g. the modular rod system).
    /// </remarks>
    internal static class RodPatches
    {
        // intentionally empty — reserved for future modular rod patches
    }
}

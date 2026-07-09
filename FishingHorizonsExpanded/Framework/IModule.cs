using StardewModdingAPI;

namespace FishingHorizonsExpanded.Framework
{
    /// <summary>A self-contained gameplay mechanic. Modules subscribe to events in <see cref="Activate"/>.</summary>
    internal interface IModule
    {
        /// <summary>The module name (for logs and config).</summary>
        string Name { get; }

        /// <summary>Whether the module is enabled in the config.</summary>
        bool IsEnabled { get; }

        /// <summary>Hook up event handlers. Called once at game launch. Handlers must check <see cref="IsEnabled"/> themselves so toggling the config mid-game works.</summary>
        void Activate(IModHelper helper);
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using osu.Framework.Graphics.Sprites;
using osu.Framework.IO.Stores;

namespace Orthomania.Patcher.Graphics
{
    /// <summary>
    /// The window's font: lazer's Torus, read from the installed lazer's <c>osu.Game.Resources.dll</c> (lazer's resources are licensed
    /// separately, so orthomania never ships them), or the framework's Roboto when lazer is not there.
    /// </summary>
    public static class PatcherFont
    {
        public static bool HasTorus { get; private set; }

        private static readonly string[] torus_weights = { "Light", "Regular", "SemiBold", "Bold" };

        /// <summary>
        /// Adds Torus from <paramref name="resourcesAssembly"/> to the game's fonts.
        /// </summary>
        /// <returns>Whether Torus is available.</returns>
        public static bool TryAddTorus(osu.Framework.Game game, string? resourcesAssembly, Action<string> log)
        {
            if (resourcesAssembly == null || !File.Exists(resourcesAssembly))
                return false;

            try
            {
                var assembly = new AssemblyLoadContext("lazer-resources").LoadFromAssemblyPath(Path.GetFullPath(resourcesAssembly));
                var store = new ResourceStore<byte[]>(new DllResourceStore(assembly));

                foreach (string weight in torus_weights)
                {
                    if (store.Get($"Fonts/Torus/Torus-{weight}.bin") == null)
                    {
                        log($"no Torus-{weight} in {resourcesAssembly}; using Roboto");
                        return false;
                    }
                }

                foreach (string weight in torus_weights)
                    game.AddFont(store, $"Fonts/Torus/Torus-{weight}");

                HasTorus = true;
                return true;
            }
            catch (Exception e) when (e is IOException or BadImageFormatException or FileLoadException or TargetInvocationException)
            {
                log($"could not read Torus from {resourcesAssembly}: {e.Message}");
                return false;
            }
        }

        public static FontUsage Get(float size, FontWeight weight = FontWeight.Regular)
        {
            if (HasTorus)
                return new FontUsage("Torus", size, weight.ToString());

            // The framework's Roboto has Regular and Bold.
            return new FontUsage("Roboto", size, weight is FontWeight.SemiBold or FontWeight.Bold ? "Bold" : "Regular");
        }
    }

    public enum FontWeight
    {
        Light,
        Regular,
        SemiBold,
        Bold,
    }
}

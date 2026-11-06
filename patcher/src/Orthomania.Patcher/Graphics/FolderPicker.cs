using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using osu.Framework.Platform;
using SDL;

namespace Orthomania.Patcher.Graphics
{
    /// <summary>
    /// The system's own folder picker (Explorer's on Windows), through SDL, which the framework already runs the window with.
    /// </summary>
    public static unsafe class FolderPicker
    {
        /// <summary>
        /// Shows the picker without waiting for it.
        /// </summary>
        /// <param name="picked">Called with the folder chosen, or null when the picker was closed, on any thread.</param>
        /// <param name="failed">Called when the picker could not be shown, on any thread.</param>
        public static void Show(GameHost host, string? startIn, Action<string?> picked, Action<string> failed)
        {
            // SDL asks for its dialogs to be started from the thread its window runs on.
            host.InputThread.Scheduler.Add(() =>
            {
                var handle = GCHandle.Alloc(new Request(picked, failed));

                try
                {
                    SDL3.SDL_ShowOpenFolderDialog(&onDone, GCHandle.ToIntPtr(handle), null, startIn, false);
                }
                catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
                {
                    handle.Free();
                    failed(e.Message);
                }
            });
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void onDone(IntPtr userdata, byte** files, int filter)
        {
            var handle = GCHandle.FromIntPtr(userdata);
            var request = (Request)handle.Target!;
            handle.Free();

            try
            {
                if (files == null)
                    request.Failed(SDL3.SDL_GetError() ?? "the folder picker could not open");
                else if (*files == null)
                    request.Picked(null);
                else
                    request.Picked(Marshal.PtrToStringUTF8((IntPtr)(*files)));
            }
            catch (Exception)
            {
                // Nothing may leave a callback from native code.
            }
        }

        private sealed record Request(Action<string?> Picked, Action<string> Failed);
    }
}

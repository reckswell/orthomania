using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Orthomania.Patcher.Folders
{
    /// <summary>
    /// A second name for an existing file: the same bytes on disk, no extra space. .NET has no API for it.
    /// </summary>
    public static partial class HardLink
    {
        /// <summary>
        /// Links <paramref name="link"/> to <paramref name="existing"/>, or copies it when the system refuses a link
        /// (another drive, a file system without links).
        /// </summary>
        /// <returns>Whether a link was made (false: copied).</returns>
        public static bool CreateOrCopy(string existing, string link)
        {
            if (TryCreate(existing, link))
                return true;

            File.Copy(existing, link, overwrite: false);
            return false;
        }

        public static bool TryCreate(string existing, string link)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    return createHardLink(link, existing, IntPtr.Zero);

                return unixLink(existing, link) == 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool createHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

        [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
        private static partial int unixLink(string existing, string link);
    }
}

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orthomania.Patcher.Settings
{
    /// <summary>
    /// Reads and writes the patcher's small JSON files. A write goes to a temporary file first, so a crash never leaves half a file.
    /// </summary>
    public static class JsonFile
    {
        public static readonly JsonSerializerOptions OPTIONS = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// The file's contents, or a new <typeparamref name="T"/> when it is missing or unreadable (<paramref name="problem"/> says which).
        /// </summary>
        public static T ReadOrDefault<T>(string path, out string? problem)
            where T : class, new()
        {
            problem = null;

            if (!File.Exists(path))
                return new T();

            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), OPTIONS) ?? new T();
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                problem = $"{Path.GetFileName(path)} could not be read ({e.Message}); starting from defaults";
                return new T();
            }
        }

        public static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, OPTIONS));
            File.Move(temp, path, overwrite: true);
        }
    }
}

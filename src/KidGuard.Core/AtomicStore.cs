using System.Text.Json;
using System.Text.Json.Serialization;

namespace KidGuard.Core;
public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = false, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}
public sealed class AtomicStore<T> where T : new()
{
    private readonly string _path;
    public AtomicStore(string path) { _path = path; Directory.CreateDirectory(Path.GetDirectoryName(path)!); }
    public T Read() => File.Exists(_path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(_path), Json.Options) ?? throw new InvalidDataException("Empty state") : new();
    public void Write(T value)
    {
        var temporary = _path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Json.Options);
            stream.Flush(true);
        }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, _path, true);
    }
}

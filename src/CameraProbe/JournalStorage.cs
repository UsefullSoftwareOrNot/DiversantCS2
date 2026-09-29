namespace CameraProbe;

internal static class JournalStorage
{
    internal static void Publish(string path, Action<FileStream> write)
    {
        // The destination becomes authoritative only after a complete durable write.
        string temporary = path + $".pending-{Guid.NewGuid():N}";
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            write(file);
            file.Flush(true);
        }
        File.Move(temporary, path, overwrite: false);
    }
}

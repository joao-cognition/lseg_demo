using System.IO;

namespace MarketDataHub.Interfaces.Impl
{
    public class FileSystemWrapper : IFileSystem
    {
        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public void WriteAllText(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }
    }
}

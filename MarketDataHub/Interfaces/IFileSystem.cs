namespace MarketDataHub.Interfaces
{
    public interface IFileSystem
    {
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        void WriteAllText(string path, string contents);
        string ReadAllText(string path);
        bool FileExists(string path);
    }
}

using System.Threading.Tasks;

namespace VRLogConsole.Scripts.Models.PersistantStore.Impl.Reader
{
    public interface IFileReader 
    {
        Task WriteFileAsync(string path, string fileName, string content);
    }
}

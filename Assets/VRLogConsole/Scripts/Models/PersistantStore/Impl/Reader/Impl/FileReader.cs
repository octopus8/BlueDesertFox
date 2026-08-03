using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace VRLogConsole.Scripts.Models.PersistantStore.Impl.Reader.Impl
{
    public class FileReader : IFileReader
    {
        public async Task WriteFileAsync(string path,string fileName,string content)
        {
            var combinedPath = GetCombinedPath(path, fileName);
        
            var stream = new FileStream(combinedPath, FileMode.Append, FileAccess.Write, FileShare.None, 4096,true);

            try
            {
                using (var streamWriter = new StreamWriter(stream))
                {
                    await streamWriter.WriteLineAsync(content);
                }   
            }
            catch (Exception exception)
            {
                Debug.Log($"File {fileName}, could not be save at path {path} with exception " + exception.Message);
            }
        }

        private static string GetCombinedPath(string path, string fileName)
        {
            var exists = Directory.Exists(path);

            if (!exists)
            {
                Directory.CreateDirectory(path);
            }
            
            return Path.Combine(path, fileName);
        }
    }
}
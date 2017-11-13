using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PackageManagement.NuGetProvider
{
    /// <summary>
    /// TODO: All the retry stuff should be merged with the retry helper in NuGet v3
    /// </summary>
    internal sealed class ZipUtility
    {
        public static void Unzip(string zipFilePath, string destinationPath)
        {
            using (ZipArchive archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    DateTimeOffset lastWriteTime = entry.LastWriteTime;
                    string fileTargetPath = Path.Combine(destinationPath, entry.FullName);
                    string subPath = Path.GetDirectoryName(fileTargetPath);
                    if (entry.Length > 0)
                    {
                        if (!Directory.Exists(subPath))
                        {
                            CreateDirectory(subPath);
                        }

                        ExecuteWithRetries(() =>
                        {
                            using (BinaryWriter writer = new BinaryWriter(GetStream(fileTargetPath, FileMode.Create)))
                            {
                                byte[] buffer = new byte[1024];
                                int numBytesRead;
                                Stream zipStream = entry.Open();
                                while ((numBytesRead = zipStream.Read(buffer, 0, 1024)) > 0)
                                {
                                    writer.Write(buffer, 0, numBytesRead);
                                }
                            }
                        });

                        ExecuteWithRetries(() => File.SetLastWriteTimeUtc(fileTargetPath, lastWriteTime.UtcDateTime));
                    }
                    else
                    {
                        if (!Directory.Exists(fileTargetPath))
                        {
                            CreateDirectory(subPath);
                        }

                        ExecuteWithRetries(() => Directory.SetLastWriteTimeUtc(fileTargetPath, lastWriteTime.UtcDateTime));
                    }
                }
            }
        }
        
        private static void CreateDirectory(string directory)
        {
            ExecuteWithRetries(() => { Directory.CreateDirectory(directory); });
        }

        private static Stream GetStream(string filePath, FileMode mode)
        {
            return ExecuteWithRetries<Stream>(() => File.Open(filePath, mode));
        }

        private static T ExecuteWithRetries<T>(Func<T> action, int retries = 3, int retryMs = 500, int retryLinearBackoff = 500)
        {
            T res = default(T);
            ExecuteWithRetries(() => res = action(), retries, retryMs, retryLinearBackoff);
            return res;
        }

        private static void ExecuteWithRetries(Action action, int retries = 3, int retryMs = 500, int retryLinearBackoff = 500)
        {
            int attempt = 0;
            while (attempt++ < retries)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception)
                {
                    if (attempt >= retries)
                    {
                        throw;
                    }

                    new System.Threading.ManualResetEvent(false).WaitOne(retryMs);
                    retryMs += retryLinearBackoff;
                    // In case this happens where the previous sleep time isn't insane (like a crazy backoff formula), reset to the start
                    // If the previous sleep was insane, it won't matter what we set this to
                    if (retryMs < 0)
                    {
                        retryMs = 500;
                    }
                }
            }

            // Should never get here
            throw new InvalidOperationException();
        }
    }
}

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Microsoft.ReactNative.Managed;

namespace RNFS
{
    [ReactModule]
    internal sealed class RNFSManager
    {
        private const int FileType = 0;
        private const int DirectoryType = 1;

        private static readonly IReadOnlyDictionary<string, Func<HashAlgorithm>> s_hashAlgorithms =
            new Dictionary<string, Func<HashAlgorithm>>
            {
                { "md5", () => MD5.Create() },
                { "sha1", () => SHA1.Create() },
                { "sha256", () => SHA256.Create() },
                { "sha384", () => SHA384.Create() },
                { "sha512", () => SHA512.Create() },
            };

        private readonly HttpClient _httpClient = new HttpClient();

        public RNFSManager() : base()
        {
        }

        [ReactConstantProvider]
        public void ConstantsViaConstantsProvider(ReactConstantProvider provider)
        {
            provider.Add("RNFSMainBundlePath", Package.Current.InstalledLocation.Path);
            provider.Add("RNFSCachesDirectoryPath", ApplicationData.Current.LocalCacheFolder.Path);
            provider.Add("RNFSRoamingDirectoryPath", ApplicationData.Current.RoamingFolder.Path);
            provider.Add("RNFSDocumentDirectoryPath", ApplicationData.Current.LocalFolder.Path);
            provider.Add("RNFSTemporaryDirectoryPath", ApplicationData.Current.TemporaryFolder.Path);
            provider.Add("RNFSFileTypeRegular", 0);
            provider.Add("RNFSFileTypeDirectory", 1);

            var external = GetFolderPathSafe(() => KnownFolders.RemovableDevices);
            if (external != null)
            {
                var externalItems = KnownFolders.RemovableDevices.GetItemsAsync().AsTask().Result;
                if (externalItems.Count > 0)
                {
                    provider.Add("RNFSExternalDirectoryPath", externalItems[0].Path);
                }
                provider.Add("RNFSExternalDirectoryPaths", externalItems.Select(i => i.Path).ToArray());
            }

            var pictures = GetFolderPathSafe(() => KnownFolders.PicturesLibrary);
            if (pictures != null)
            {
                provider.Add("RNFSPicturesDirectoryPath", pictures);
            }

        }

       [ReactMethod]
        public async void writeFile(string filepath, string base64Content, JObject options, IReactPromise<JSValue> promise)
        {
            try
            {
                // TODO: open file on background thread?
                using (var file = File.OpenWrite(filepath))
                {
                    var data = Convert.FromBase64String(base64Content);
                    await file.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                }

                promise.Resolve(new JSValue());
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        [ReactMethod] public void createFile(string filepath, IReactPromise<object> promise)
        {
            using (var file = File.Create(filepath)) { };
            promise.Resolve(null);
        }

        [ReactMethod]
        public async void appendFile(string filepath, string base64Content)
        {
            // TODO: open file on background thread?
            using (var file = File.Open(filepath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                var data = Convert.FromBase64String(base64Content);
                await file.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
            }
        }

        [ReactMethod]
        public async void write(string filepath, string base64Content, int position, IReactPromise<JSValue> promise)
        {
            try
            {
                // TODO: open file on background thread?
                using (var file = File.OpenWrite(filepath))
                {
                    if (position >= 0)
                    {
                        file.Position = position;
                    }

                    var data = Convert.FromBase64String(base64Content);
                    await file.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                }

                promise.Resolve(new JSValue());
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        [ReactMethod]
        public void exists(string filepath, IReactPromise<JSValue> promise)
        {
            try
            {
                promise.Resolve(new JSValue(File.Exists(filepath) || Directory.Exists(filepath)));
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        [ReactMethod]
        public async void readFile(string filepath, IReactPromise<JSValue> promise)
        {
            try
            {
                if (!File.Exists(filepath))
                {
                    RejectFileNotFound(promise, filepath);
                    return;
                }

                // TODO: open file on background thread?
                string base64Content;
                using (var file = File.OpenRead(filepath))
                {
                    var length = (int)file.Length;
                    var buffer = new byte[length];
                    await file.ReadAsync(buffer, 0, length).ConfigureAwait(false);
                    base64Content = Convert.ToBase64String(buffer);
                }

                promise.Resolve(new JSValue(base64Content));
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex); 
            }
        }

        [ReactMethod]
        public async void read(string filepath, int length, int position, IReactPromise<JSValue> promise)
        {
            try
            {
                if (!File.Exists(filepath))
                {
                    RejectFileNotFound(promise, filepath);
                    return;
                }

                // TODO: open file on background thread?
                string base64Content;
                using (var file = File.OpenRead(filepath))
                {
                    file.Position = position;
                    var buffer = new byte[length];
                    await file.ReadAsync(buffer, 0, length).ConfigureAwait(false);
                    base64Content = Convert.ToBase64String(buffer);
                }

                promise.Resolve(new JSValue(base64Content));
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        [ReactMethod]
        public async void hash(string filepath, string algorithm, IReactPromise<JSValue> promise)
        {
            var hashAlgorithmFactory = default(Func<HashAlgorithm>);
            if (!s_hashAlgorithms.TryGetValue(algorithm, out hashAlgorithmFactory))
            {
                ReactError error = new ReactError();
                error.Code = null;
                error.Message = "Invalid hash algorithm.";
                promise.Reject(error);
                return;
            }

            try
            {
                if (!File.Exists(filepath))
                {
                    RejectFileNotFound(promise, filepath);
                    return;
                }

                await Task.Run(() =>
                {
                    var hexBuilder = new StringBuilder();
                    using (var hashAlgorithm = hashAlgorithmFactory())
                    {
                        hashAlgorithm.Initialize();
                        var hash = default(byte[]);
                        using (var file = File.OpenRead(filepath))
                        {
                            hash = hashAlgorithm.ComputeHash(file);
                        }

                        foreach (var b in hash)
                        {
                            hexBuilder.Append(string.Format("{0:x2}", b));
                        }
                    }

                    promise.Resolve(new JSValue(hexBuilder.ToString()));
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        [ReactMethod]
        public void moveFile(string filepath, string destPath, JObject options, IReactPromise<JSValue> promise)
        {
            try
            {
                // TODO: move file on background thread?
                File.Move(filepath, destPath);
                promise.Resolve(new JSValue(true));
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        [ReactMethod]
        public async void copyFile(string filepath, string destPath, JObject options)
        {
            await Task.Run(() => File.Copy(filepath, destPath)).ConfigureAwait(false);
        }

        [ReactMethod]
        public async void readDir(string directory, IReactPromise<JSValue> promise)
        {
            try
            {
                await Task.Run(() =>
                {
                    var info = new DirectoryInfo(directory);
                    if (!info.Exists)
                    {
                        ReactError error = new ReactError();
                        error.Code = null;
                        error.Message = "Folder does not exist";
                        promise.Reject(error);
                        return;
                    }

                    var fileMaps = new List<JSValue>();
                    foreach (var item in info.EnumerateFileSystemInfos())
                    {
                        var fileMap = new Dictionary<string, JSValue>
                        {
                            { "mtime", new JSValue(ConvertToUnixTimestamp(item.LastWriteTime)) },
                            { "name", new JSValue(item.Name) },
                            { "path", new JSValue(item.FullName) },
                        };

                        var fileItem = item as FileInfo;
                        if (fileItem != null)
                        {
                            fileMap.Add("type", new JSValue(FileType));
                            fileMap.Add("size", new JSValue(fileItem.Length));
                        }
                        else
                        {
                            fileMap.Add("type", new JSValue(DirectoryType));
                            fileMap.Add("size", new JSValue(0));
                        }

                        fileMaps.Add(new JSValue(fileMap));
                    }

                    promise.Resolve(new JSValue(fileMaps));
                });
            }
            catch (Exception ex)
            {
                Reject(promise, directory, ex);
            }
        }

        [ReactMethod]
        public void stat(string filepath, IReactPromise<JSValue> promise)
        {
            try
            {
                FileSystemInfo fileSystemInfo = new FileInfo(filepath);
                if (!fileSystemInfo.Exists)
                {
                    fileSystemInfo = new DirectoryInfo(filepath);
                    if (!fileSystemInfo.Exists)
                    {
                        ReactError error = new ReactError();
                        error.Code = null;
                        error.Message = "File does not exist";
                        promise.Reject(error);
                        return;
                    }
                }

                var fileInfo = fileSystemInfo as FileInfo;
                var statMap = new Dictionary<string, JSValue>
                {
                    { "ctime", new JSValue(ConvertToUnixTimestamp(fileSystemInfo.CreationTime)) },
                    { "mtime", new JSValue(ConvertToUnixTimestamp(fileSystemInfo.LastWriteTime)) },
                    { "size", new JSValue(fileInfo?.Length ?? 0) },
                    { "type", new JSValue(fileInfo != null ? FileType: DirectoryType) },
                };

                promise.Resolve(new JSValue(statMap));
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        [ReactMethod]
        public async void unlink(string filepath, IReactPromise<JSValue> promise)
        {
            try
            {
                var directoryInfo = new DirectoryInfo(filepath);
                var fileInfo = default(FileInfo);
                if (directoryInfo.Exists)
                {
                    await Task.Run(() => Directory.Delete(filepath, true)).ConfigureAwait(false);
                }
                else if ((fileInfo = new FileInfo(filepath)).Exists)
                {
                    await Task.Run(() => File.Delete(filepath)).ConfigureAwait(false);
                }
                else
                {
                    ReactError error = new ReactError();
                    error.Code = null;
                    error.Message = "File does not exist";
                    promise.Reject(error);
                    return;
                }

                promise.Resolve(new JSValue());
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        [ReactMethod]
        public async void mkdir(string filepath, JObject options, IReactPromise<JSValue> promise)
        {
            try
            {
                await Task.Run(() => Directory.CreateDirectory(filepath)).ConfigureAwait(false);
                promise.Resolve(new JSValue());
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        [ReactMethod]
        public void downloadFile(JObject options, IReactPromise<JSValue> promise)
        {
            var filepath = options.Value<string>("toFile");

            try
            {
                var url = new Uri(options.Value<string>("fromUrl"));
                var jobId = options.Value<int>("jobId");
                var headers = (JObject)options["headers"];
                var progressDivider = options.Value<int>("progressDivider");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.Value<string>());
                }

                //await _tasks.AddAndInvokeAsync(jobId, token => 
                //    ProcessRequestAsync(promise, request, filepath, jobId, progressDivider, token));
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        [ReactMethod]
        public void stopDownload(int jobId)
        {
        }

        [ReactMethod]
        public async void getFSInfo(IReactPromise<object> promise)
        {
            try
            {
                var properties = await ApplicationData.Current.LocalFolder.Properties.RetrievePropertiesAsync(
                    new[] 
                    {
                        "System.FreeSpace",
                        "System.Capacity",
                    })
                    .AsTask()
                    .ConfigureAwait(false);

                promise.Resolve(new JObject
                {
                    { "freeSpace", (ulong)properties["System.FreeSpace"] },
                    { "totalSpace", (ulong)properties["System.Capacity"] },
                });
            }
            catch (Exception)
            {
                ReactError error = new ReactError();
                error.Code = null;
                error.Message = "getFSInfo is not available";
                promise.Reject(error);
            }
        }

        [ReactMethod]
        public async void touch(string filepath, double mtime, double ctime, IReactPromise<JSValue> promise)
        {
            try
            {
                await Task.Run(() =>
                {
                    var fileInfo = new FileInfo(filepath);
                    if (!fileInfo.Exists)
                    {
                        using (File.Create(filepath)) { }
                    }

                    fileInfo.CreationTimeUtc = ConvertFromUnixTimestamp(ctime);
                    fileInfo.LastWriteTimeUtc = ConvertFromUnixTimestamp(mtime);

                    promise.Resolve(new JSValue(fileInfo.FullName));
                });
            }
            catch (Exception ex)
            {
                Reject(promise, filepath, ex);
            }
        }

        private async Task ProcessRequestAsync(IReactPromise<object> promise, HttpRequestMessage request, string filepath, int jobId, int progressIncrement, CancellationToken token)
        {
            try
            {
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    var headersMap = new JObject();
                    foreach (var header in response.Headers)
                    {
                        headersMap.Add(header.Key, string.Join(",", header.Value));
                    }

                    var contentLength = response.Content.Headers.ContentLength;
                    SendEvent($"DownloadBegin-{jobId}", new JObject
                    {
                        { "jobId", jobId },
                        { "statusCode", (int)response.StatusCode },
                        { "contentLength", contentLength },
                        { "headers", headersMap },
                    });

                    // TODO: open file on background thread?
                    long totalRead = 0;
                    using (var fileStream = File.OpenWrite(filepath))
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        var contentLengthForProgress = contentLength ?? -1;
                        var nextProgressIncrement = progressIncrement;
                        var buffer = new byte[8 * 1024];
                        var read = 0;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested();

                            await fileStream.WriteAsync(buffer, 0, read);
                            if (contentLengthForProgress >= 0)
                            {
                                totalRead += read;
                                if (totalRead * 100 / contentLengthForProgress >= nextProgressIncrement ||
                                    totalRead == contentLengthForProgress)
                                {
                                    SendEvent("DownloadProgress-" + jobId, new JObject
                                    {
                                        { "jobId", jobId },
                                        { "contentLength", contentLength },
                                        { "bytesWritten", totalRead },
                                    });

                                    nextProgressIncrement += progressIncrement;
                                }
                            }
                        }
                    }

                    promise.Resolve(new JObject
                    {
                        { "jobId", jobId },
                        { "statusCode", (int)response.StatusCode },
                        { "bytesWritten", totalRead },
                    });
                }
            }
            finally
            {
                request.Dispose();
            }
        }

        private void Reject(IReactPromise<JSValue> promise, String filepath, Exception ex)
        {
            if (ex is FileNotFoundException) {
                RejectFileNotFound(promise, filepath);
                return;
            }
            ReactError error = new ReactError();
            error.Exception = ex;
            promise.Reject(error);
        }

        private void RejectFileNotFound(IReactPromise<JSValue> promise, String filepath)
        {
            ReactError error = new ReactError();
            error.Message = "ENOENT: no such file or directory, open '" + filepath + "'";
            error.Code = "ENOENT";
            promise.Reject(error);
        }

        private void SendEvent(string eventName, JObject eventData)
        {
        }

        private static string GetFolderPathSafe(Func<StorageFolder> getFolder)
        {
            try
            {
                return getFolder().Path;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static double ConvertToUnixTimestamp(DateTime date)
        {
            var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            var diff = date.ToUniversalTime() - origin;
            return Math.Floor(diff.TotalSeconds);
        }

        public static DateTime ConvertFromUnixTimestamp(double timestamp)
        {
            var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            var diff = TimeSpan.FromSeconds(timestamp);
            var dateTimeUtc = origin + diff;
            return dateTimeUtc.ToLocalTime();
        }
    }
}

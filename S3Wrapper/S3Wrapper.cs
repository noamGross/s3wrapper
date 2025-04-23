using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Amazon.Runtime;
using System.Threading.Channels;

namespace S3Wrapper
{
    public interface IS3Wrapper
    {
        event EventHandler<string>? NewFileEvent;
        Task WriteAsync(string fileName, byte[] data);
        Task WriteAsync<T>(string fileName, T obj);
        Task WriteLargeFileAsync(string fileName, Stream stream);
        Task<byte[]> ReadAsync(string fileName);
        Task<T?> ReadAsync<T>(string fileName);
        Task<Stream> ReadLargeFileAsync(string fileName);
        Task StartMonitoringAsync();
        Task StopMonitoringAsync();
    }

    public class S3Wrapper : IS3Wrapper
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private const int PART_SIZE = 5 * 1024 * 1024; // 5MB minimum part size for S3
        private readonly Channel<string> _eventChannel;
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;

        public event EventHandler<string>? NewFileEvent;

        public S3Wrapper(string bucketName, IAmazonS3 s3Client)
        {
            _bucketName = bucketName;
            _s3Client = s3Client;
            _eventChannel = Channel.CreateUnbounded<string>();
        }

        public async Task StartMonitoringAsync()
        {
            if (_monitoringTask != null)
                return;

            _monitoringCts = new CancellationTokenSource();
            _monitoringTask = Task.Run(async () =>
            {
                var lastModified = DateTime.MinValue;
                var token = _monitoringCts.Token;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var request = new ListObjectsV2Request
                        {
                            BucketName = _bucketName
                        };

                        var response = await _s3Client.ListObjectsV2Async(request, token);
                        
                        foreach (var obj in response.S3Objects)
                        {
                            if (obj.LastModified > lastModified)
                            {
                                lastModified = obj.LastModified;
                                await _eventChannel.Writer.WriteAsync(obj.Key, token);
                            }
                        }

                        await Task.Delay(TimeSpan.FromSeconds(5), token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Log the error and continue monitoring
                        Console.WriteLine($"Error monitoring bucket: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(30), token);
                    }
                }
            });

            // Start processing events
            _ = Task.Run(async () =>
            {
                await foreach (var fileName in _eventChannel.Reader.ReadAllAsync())
                {
                    OnNewFileEvent(fileName);
                }
            });
        }

        public async Task StopMonitoringAsync()
        {
            if (_monitoringCts == null)
                return;

            _monitoringCts.Cancel();
            _eventChannel.Writer.Complete();

            if (_monitoringTask != null)
            {
                await _monitoringTask;
                _monitoringTask = null;
            }

            _monitoringCts.Dispose();
            _monitoringCts = null;
        }

        public async Task WriteAsync(string fileName, byte[] data)
        {
            using var stream = new MemoryStream(data);
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = fileName,
                InputStream = stream
            };

            await _s3Client.PutObjectAsync(request);
            OnNewFileEvent(fileName);
        }

        public async Task WriteAsync<T>(string fileName, T obj)
        {
            var json = JsonSerializer.Serialize(obj);
            var data = System.Text.Encoding.UTF8.GetBytes(json);
            await WriteAsync(fileName, data);
        }

        public async Task WriteLargeFileAsync(string fileName, Stream stream)
        {
            // Initiate multipart upload
            var initiateRequest = new InitiateMultipartUploadRequest
            {
                BucketName = _bucketName,
                Key = fileName
            };

            var initResponse = await _s3Client.InitiateMultipartUploadAsync(initiateRequest);
            var uploadId = initResponse.UploadId;

            try
            {
                var partETags = new List<PartETag>();
                var partNumber = 1;
                var buffer = new byte[PART_SIZE];

                while (true)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    using var partStream = new MemoryStream(buffer, 0, bytesRead);
                    var uploadPartRequest = new UploadPartRequest
                    {
                        BucketName = _bucketName,
                        Key = fileName,
                        UploadId = uploadId,
                        PartNumber = partNumber,
                        InputStream = partStream,
                        PartSize = bytesRead
                    };

                    var uploadPartResponse = await _s3Client.UploadPartAsync(uploadPartRequest);
                    partETags.Add(new PartETag(partNumber, uploadPartResponse.ETag));
                    partNumber++;
                }

                // Complete multipart upload
                var completeRequest = new CompleteMultipartUploadRequest
                {
                    BucketName = _bucketName,
                    Key = fileName,
                    UploadId = uploadId,
                    PartETags = partETags
                };

                await _s3Client.CompleteMultipartUploadAsync(completeRequest);
                OnNewFileEvent(fileName);
            }
            catch (Exception)
            {
                // Abort multipart upload in case of failure
                var abortRequest = new AbortMultipartUploadRequest
                {
                    BucketName = _bucketName,
                    Key = fileName,
                    UploadId = uploadId
                };
                await _s3Client.AbortMultipartUploadAsync(abortRequest);
                throw;
            }
        }

        public async Task<byte[]> ReadAsync(string fileName)
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = fileName
            };

            using var response = await _s3Client.GetObjectAsync(request);
            using var responseStream = response.ResponseStream;
            using var memoryStream = new MemoryStream();
            
            await responseStream.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }

        public async Task<Stream> ReadLargeFileAsync(string fileName)
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = fileName
            };

            var response = await _s3Client.GetObjectAsync(request);
            return response.ResponseStream;
        }

        public async Task<T?> ReadAsync<T>(string fileName)
        {
            var data = await ReadAsync(fileName);
            var json = System.Text.Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<T>(json);
        }

        protected virtual void OnNewFileEvent(string fileName)
        {
            NewFileEvent?.Invoke(this, fileName);
        }
    }

    public static class S3WrapperExtensions
    {
        public static IServiceCollection AddS3Wrapper(this IServiceCollection services, 
            string bucketName,
            string endpoint,
            string accessKey,
            string secretKey,
            bool useHttps = false)
        {
            var config = new AmazonS3Config
            {
                ServiceURL = $"{(useHttps ? "https" : "http")}://{endpoint}",
                ForcePathStyle = true // Required for MinIO
            };

            var credentials = new BasicAWSCredentials(accessKey, secretKey);
            var s3Client = new AmazonS3Client(credentials, config);

            services.AddSingleton<IAmazonS3>(s3Client);
            services.AddSingleton<IS3Wrapper, S3Wrapper>(sp => 
                new S3Wrapper(bucketName, sp.GetRequiredService<IAmazonS3>()));
            
            return services;
        }
    }
} 
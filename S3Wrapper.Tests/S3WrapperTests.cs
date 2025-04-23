using Amazon.S3;
using Amazon.Runtime;
using Xunit;
using S3Wrapper;

namespace S3Wrapper.Tests;

[CollectionDefinition("MinIO")]
public class MinIOCollection : ICollectionFixture<MinIOFixture> { }

[Collection("MinIO")]
public class S3WrapperTests : IAsyncLifetime
{
    private readonly MinIOFixture _fixture;
    private readonly IAmazonS3 _s3Client;
    private readonly S3Wrapper _s3Wrapper;
    private readonly List<string> _eventFiles = new();
    private bool _monitoringStarted = false;

    public S3WrapperTests(MinIOFixture fixture)
    {
        _fixture = fixture;
        
        var config = new AmazonS3Config
        {
            ServiceURL = $"http://{_fixture.Endpoint}",
            ForcePathStyle = true
        };

        var credentials = new BasicAWSCredentials(_fixture.AccessKey, _fixture.SecretKey);
        _s3Client = new AmazonS3Client(credentials, config);
        _s3Wrapper = new S3Wrapper(_fixture.BucketName, _s3Client);
        
        _s3Wrapper.NewFileEvent += (sender, fileName) => _eventFiles.Add(fileName);
    }

    public async Task InitializeAsync()
    {
        await _s3Wrapper.StartMonitoringAsync();
        _monitoringStarted = true;
    }

    public async Task DisposeAsync()
    {
        if (_monitoringStarted)
        {
            await _s3Wrapper.StopMonitoringAsync();
        }
        _s3Client.Dispose();
    }

    [Fact]
    public async Task WriteAndReadBinaryData()
    {
        // Arrange
        var fileName = "test.bin";
        var expectedData = new byte[] { 1, 2, 3, 4 };

        // Act
        await _s3Wrapper.WriteAsync(fileName, expectedData);
        var actualData = await _s3Wrapper.ReadAsync(fileName);

        // Assert
        Assert.Equal(expectedData, actualData);
        Assert.Contains(fileName, _eventFiles);
    }

    [Fact]
    public async Task WriteAndReadObject()
    {
        // Arrange
        var fileName = "test.json";
        var expectedObject = new TestObject { Name = "Test", Value = 42 };

        // Act
        await _s3Wrapper.WriteAsync(fileName, expectedObject);
        var actualObject = await _s3Wrapper.ReadAsync<TestObject>(fileName);

        // Assert
        Assert.NotNull(actualObject);
        Assert.Equal(expectedObject.Name, actualObject.Name);
        Assert.Equal(expectedObject.Value, actualObject.Value);
        Assert.Contains(fileName, _eventFiles);
    }

    [Fact]
    public async Task WriteAndReadLargeFile()
    {
        // Arrange
        var fileName = "large-file.bin";
        var expectedData = new byte[10 * 1024 * 1024]; // 10MB
        new Random().NextBytes(expectedData);
        using var inputStream = new MemoryStream(expectedData);

        // Act
        await _s3Wrapper.WriteLargeFileAsync(fileName, inputStream);
        using var outputStream = await _s3Wrapper.ReadLargeFileAsync(fileName);
        using var memoryStream = new MemoryStream();
        await outputStream.CopyToAsync(memoryStream);
        var actualData = memoryStream.ToArray();

        // Assert
        Assert.Equal(expectedData, actualData);
        Assert.Contains(fileName, _eventFiles);
    }

    [Fact]
    public async Task MonitorExternalChanges()
    {
        // Arrange
        var fileName = "external-file.txt";
        var expectedData = new byte[] { 5, 6, 7, 8 };

        // Act
        // Simulate external file creation
        using var stream = new MemoryStream(expectedData);
        var request = new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = fileName,
            InputStream = stream
        };
        await _s3Client.PutObjectAsync(request);

        // Wait for monitoring to detect the change
        await Task.Delay(TimeSpan.FromSeconds(6));

        // Assert
        Assert.Contains(fileName, _eventFiles);
    }

    [Fact]
    public async Task WriteToExistingFile_ThrowsException()
    {
        // Arrange
        var fileName = "existing-file.txt";
        var data = new byte[] { 1, 2, 3, 4 };
        await _s3Wrapper.WriteAsync(fileName, data);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _s3Wrapper.WriteAsync(fileName, data));
        
        Assert.Contains(fileName, exception.Message);
        Assert.Contains(_fixture.BucketName, exception.Message);
    }

    private class TestObject
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
} 
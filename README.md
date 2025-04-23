# S3Wrapper

A .NET 8 wrapper for S3-compatible storage (AWS S3 and MinIO) that provides simple methods for reading and writing files and objects.

## Features

- Write binary data to S3/MinIO
- Write objects to S3/MinIO (serialized as JSON)
- Read binary data from S3/MinIO
- Read objects from S3/MinIO (deserialized from JSON)
- Event notification when new files are written
- Monitor bucket for external file changes
- Dependency Injection support
- MinIO-specific configuration support
- Large file support (multipart upload and streaming)
- Comprehensive logging support

## Installation

Add the required NuGet packages to your project:

```bash
dotnet add package AWSSDK.S3
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package Microsoft.Extensions.Logging.Abstractions
```

## Usage

### MinIO Setup

First, ensure you have MinIO running. You can run it locally using Docker:

```bash
docker run -p 9000:9000 -p 9001:9001 minio/minio server /data --console-address ":9001"
```

### Basic Usage

```csharp
// Configure MinIO client
var config = new AmazonS3Config
{
    ServiceURL = "http://localhost:9000",
    ForcePathStyle = true // Required for MinIO
};

var credentials = new BasicAWSCredentials("minioadmin", "minioadmin");
var s3Client = new AmazonS3Client(credentials, config);

// Create the wrapper instance
var s3Wrapper = new S3Wrapper("your-bucket-name", s3Client, logger);

// Subscribe to new file events
s3Wrapper.NewFileEvent += (sender, fileName) => 
{
    Console.WriteLine($"New file written: {fileName}");
};

// Start monitoring the bucket for external changes
await s3Wrapper.StartMonitoringAsync();

// Write binary data
await s3Wrapper.WriteAsync("test.bin", new byte[] { 1, 2, 3, 4 });

// Write an object
var myObject = new { Name = "Test", Value = 42 };
await s3Wrapper.WriteAsync("test.json", myObject);

// Read binary data
var data = await s3Wrapper.ReadAsync("test.bin");

// Read an object
var result = await s3Wrapper.ReadAsync<MyClass>("test.json");

// Stop monitoring when done
await s3Wrapper.StopMonitoringAsync();
```

### Large File Support

```csharp
// Writing a large file
using var fileStream = File.OpenRead("large-file.bin");
await s3Wrapper.WriteLargeFileAsync("large-file.bin", fileStream);

// Reading a large file
using var responseStream = await s3Wrapper.ReadLargeFileAsync("large-file.bin");
using var outputStream = File.Create("output.bin");
await responseStream.CopyToAsync(outputStream);
```

### Dependency Injection Setup with MinIO

```csharp
// In your Program.cs or Startup.cs
public void ConfigureServices(IServiceCollection services)
{
    // Configure logging
    services.AddLogging(logging =>
    {
        logging.AddConsole();
        logging.AddDebug();
        // Add other logging providers as needed
    });

    // Register S3Wrapper with MinIO configuration
    services.AddS3Wrapper(
        bucketName: "your-bucket-name",
        endpoint: "localhost:9000",  // MinIO server endpoint
        accessKey: "minioadmin",     // MinIO access key
        secretKey: "minioadmin",     // MinIO secret key
        useHttps: false              // Set to true if using HTTPS
    );
}

// In your service or controller
public class MyService
{
    private readonly IS3Wrapper _s3Wrapper;
    private readonly ILogger<MyService> _logger;

    public MyService(IS3Wrapper s3Wrapper, ILogger<MyService> logger)
    {
        _s3Wrapper = s3Wrapper;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        // Start monitoring for external changes
        await _s3Wrapper.StartMonitoringAsync();
    }

    public async Task DoSomething()
    {
        await _s3Wrapper.WriteAsync("test.txt", new byte[] { 1, 2, 3 });
    }

    public async Task HandleLargeFile(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        await _s3Wrapper.WriteLargeFileAsync(Path.GetFileName(filePath), fileStream);
    }

    public async Task DisposeAsync()
    {
        // Stop monitoring when done
        await _s3Wrapper.StopMonitoringAsync();
    }
}
```

## Logging

The S3Wrapper includes comprehensive logging support:

- Operation start and end logging
- Success and error logging
- Detailed exception information
- Structured logging with parameters
- Different log levels (Information, Error, Debug)

Logging is performed for:
- File operations (read/write)
- Object operations
- Large file operations
- Bucket monitoring
- Error conditions
- Event notifications

Example log output:
```
info: S3Wrapper.S3Wrapper[0]
      Starting write operation for file test.txt in bucket my-bucket
info: S3Wrapper.S3Wrapper[0]
      Successfully wrote file test.txt to bucket my-bucket
```

## MinIO Configuration

The wrapper is configured specifically for MinIO with:
- Path-style addressing (`ForcePathStyle = true`)
- Custom endpoint configuration
- Basic authentication support
- Optional HTTPS support

## Large File Handling

The wrapper supports large files through:
- Multipart upload for writing large files
- Streaming for reading large files
- 5MB part size for efficient uploads
- Automatic cleanup of failed uploads
- Memory-efficient streaming operations
- No need to specify file size - handled automatically

## Bucket Monitoring

The wrapper can monitor the bucket for changes made outside the application:
- Polls the bucket every 5 seconds for new files
- Uses a channel-based event system for efficient event handling
- Automatically detects new and modified files
- Graceful error handling with automatic retry
- Can be started and stopped as needed

## Thread Safety

The S3Wrapper is thread-safe because:
- The AWS S3 client is thread-safe by design
- All operations are stateless
- The wrapper is registered as a singleton in the DI container
- Event handling is thread-safe

## Requirements

- .NET 8.0 or later
- AWS SDK for .NET
- MinIO server (or any S3-compatible storage)
- Microsoft.Extensions.DependencyInjection (for DI support)
- Microsoft.Extensions.Logging.Abstractions (for logging support) 
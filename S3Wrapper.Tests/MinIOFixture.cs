using Testcontainers;
using Xunit;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Amazon.S3;
using Amazon.Runtime;

namespace S3Wrapper.Tests;

public class MinIOFixture : IAsyncLifetime
{
    private readonly IContainer _minioContainer;
    public string Endpoint => $"localhost:{_minioContainer.GetMappedPublicPort(9000)}";
    public string AccessKey => "minioadmin";
    public string SecretKey => "minioadmin";
    public string BucketName => "test-bucket";

    public MinIOFixture()
    {
        _minioContainer = new ContainerBuilder()
            .WithImage("minio/minio:latest")
            .WithPortBinding(9000, true)
            .WithPortBinding(9001, true)
            .WithCommand("server", "/data", "--console-address", ":9001")
            .WithEnvironment("MINIO_ROOT_USER", AccessKey)
            .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _minioContainer.StartAsync();
        
        // Create the test bucket using AWS SDK
        var config = new AmazonS3Config
        {
            ServiceURL = $"http://{Endpoint}",
            ForcePathStyle = true
        };

        var credentials = new BasicAWSCredentials(AccessKey, SecretKey);
        using var s3Client = new AmazonS3Client(credentials, config);
        
        try
        {
            await s3Client.PutBucketAsync(BucketName);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyOwnedByYou")
        {
            // Bucket already exists, which is fine
        }
    }

    public async Task DisposeAsync()
    {
        await _minioContainer.DisposeAsync();
    }
} 
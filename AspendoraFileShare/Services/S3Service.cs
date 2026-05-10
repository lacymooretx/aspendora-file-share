using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;

namespace AspendoraFileShare.Services;

public class S3Service
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<S3Service> _logger;

    public S3Service(IConfiguration configuration, ILogger<S3Service> logger)
    {
        _logger = logger;
        _bucketName = configuration["S3:BucketName"]!;

        var config = new AmazonS3Config
        {
            ServiceURL = configuration["S3:ServiceUrl"],
            ForcePathStyle = true
        };

        var credentials = new BasicAWSCredentials(
            configuration["S3:AccessKey"],
            configuration["S3:SecretKey"]
        );

        _s3Client = new AmazonS3Client(credentials, config);
    }

    public async Task<string> InitiateMultipartUploadAsync(string shareId, string fileName, string mimeType)
    {
        var key = $"file-share/{shareId}/{fileName}";
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
            ContentType = mimeType
        };

        var response = await _s3Client.InitiateMultipartUploadAsync(request);
        return response.UploadId;
    }

    public async Task<string> UploadPartAsync(string key, string uploadId, int partNumber, Stream data)
    {
        var request = new UploadPartRequest
        {
            BucketName = _bucketName,
            Key = key,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = data
        };

        var response = await _s3Client.UploadPartAsync(request);
        return response.ETag;
    }

    public async Task CompleteMultipartUploadAsync(string key, string uploadId, List<PartETag> parts)
    {
        var request = new CompleteMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
            UploadId = uploadId,
            PartETags = parts
        };

        await _s3Client.CompleteMultipartUploadAsync(request);
    }

    public string GeneratePresignedUrlForPart(string key, string uploadId, int partNumber)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddHours(2),
            Protocol = Protocol.HTTPS,
            UploadId = uploadId,
            PartNumber = partNumber
        };

        var url = _s3Client.GetPreSignedURL(request);
        _logger.LogDebug("Generated presigned URL for part {PartNumber}: {Url}", partNumber, url.Substring(0, Math.Min(100, url.Length)) + "...");
        return url;
    }

    public List<string> GeneratePresignedUrlsForUpload(string key, string uploadId, int totalParts)
    {
        var urls = new List<string>();
        for (int i = 1; i <= totalParts; i++)
        {
            urls.Add(GeneratePresignedUrlForPart(key, uploadId, i));
        }
        return urls;
    }

    public async Task<Stream> GetFileAsync(string s3Key)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = s3Key
        };

        var response = await _s3Client.GetObjectAsync(request);
        return response.ResponseStream;
    }

    public string GeneratePresignedDownloadUrl(string s3Key, string fileName, int expiresInMinutes = 60)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = s3Key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(expiresInMinutes),
            Protocol = Protocol.HTTPS,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = $"attachment; filename=\"{fileName}\""
            }
        };

        return _s3Client.GetPreSignedURL(request);
    }

    public async Task DeleteFileAsync(string s3Key)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = s3Key
        };

        await _s3Client.DeleteObjectAsync(request);
    }

    public async Task DeleteShareFilesAsync(string shareId)
    {
        var listRequest = new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = $"file-share/{shareId}/"
        };

        var listResponse = await _s3Client.ListObjectsV2Async(listRequest);

        foreach (var obj in listResponse.S3Objects)
        {
            await DeleteFileAsync(obj.Key);
        }
    }

    public string GenerateShortId()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Range(0, 8).Select(_ => chars[random.Next(chars.Length)]).ToArray());
    }
}

using ParityBench.NET.Application.AlternateContracts;
using ParityBench.NET.Domain.AlternateContracts;

namespace ParityBench.NET.Infrastructure;

public sealed class FileBackedContractPayloadFactory
{
    private readonly string tempRoot;

    public FileBackedContractPayloadFactory(string? tempRoot = null)
    {
        this.tempRoot = string.IsNullOrWhiteSpace(tempRoot)
            ? Path.Combine(Path.GetTempPath(), "ParityBenchNET", "contract-payloads")
            : tempRoot;
    }

    public async Task<ContractPayload> CreateAsync(
        PayloadFormat format,
        string contentType,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writeAsync);

        Directory.CreateDirectory(tempRoot);
        string payloadPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.payload");

        try
        {
            await using (FileStream writeStream = new FileStream(
                payloadPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await writeAsync(writeStream, cancellationToken).ConfigureAwait(false);
            }

            long contentLength = new FileInfo(payloadPath).Length;
            return new ContractPayload(
                format,
                contentType,
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    Stream readStream = new FileStream(
                        payloadPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 81920,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    return ValueTask.FromResult(readStream);
                },
                contentLength,
                () => DeletePayloadAsync(payloadPath));
        }
        catch
        {
            DeletePayload(payloadPath);
            throw;
        }
    }

    private static ValueTask DeletePayloadAsync(string payloadPath)
    {
        DeletePayload(payloadPath);
        return ValueTask.CompletedTask;
    }

    private static void DeletePayload(string payloadPath)
    {
        if (File.Exists(payloadPath))
        {
            File.Delete(payloadPath);
        }
    }
}

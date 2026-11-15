namespace SlimCluster.Consensus.Raft;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Uses a shared directory whose filesystem supports atomic directory renames and exclusive file creation across hosts.
/// Use the same directory on all voters and retain its contents for the lifetime of the cluster.
/// </summary>
public sealed class SharedFileRaftBootstrapStore : IRaftBootstrapStore
{
    private readonly string _directory;

    public SharedFileRaftBootstrapStore(string directory) => _directory = Path.GetFullPath(directory);

    public async Task<string[]?> GetOrPublishVoters(string clusterId, string[]? proposedVoters, CancellationToken token)
    {
        var directory = GetClusterDirectory(clusterId);
        var rosterDirectory = Path.Combine(directory, "roster");
        var path = Path.Combine(rosterDirectory, "voters.json");
        if (proposedVoters != null && !File.Exists(path))
        {
            if (!Directory.Exists(_directory))
            {
                throw new DirectoryNotFoundException($"The shared Raft bootstrap directory '{_directory}' is unavailable.");
            }
            Directory.CreateDirectory(directory);
            await Publish(rosterDirectory, proposedVoters, token);
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(await File.ReadAllBytesAsync(path, token))
                ?? throw new InvalidDataException("The published Raft voter list is empty.");
        }
        catch (FileNotFoundException) when (proposedVoters == null)
        {
            return null;
        }
        catch (DirectoryNotFoundException) when (proposedVoters == null)
        {
            return null;
        }
    }

    public Task MarkVoterReady(string clusterId, string voterId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = GetReadyPath(clusterId, voterId);
        try
        {
            using var marker = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            marker.Flush(flushToDisk: true);
        }
        catch (IOException) when (File.Exists(path))
        {
            // A ready marker has no content, so its existence is enough.
        }
        return Task.CompletedTask;
    }

    public Task<bool> AreVotersReady(string clusterId, string[] voterIds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(voterIds.All(id => File.Exists(GetReadyPath(clusterId, id))));
    }

    private string GetClusterDirectory(string clusterId) => Path.Combine(_directory, Hash(clusterId));

    private string GetReadyPath(string clusterId, string voterId) => Path.Combine(GetClusterDirectory(clusterId), $"{Hash(voterId)}.ready");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task Publish(string rosterDirectory, string[] voters, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var temporaryDirectory = $"{rosterDirectory}.{Guid.NewGuid():N}.tmp";
        var temporaryPath = Path.Combine(temporaryDirectory, "voters.json");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, voters, cancellationToken: token);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                token.ThrowIfCancellationRequested();
                // The winning directory is nonempty, so a competing rename cannot replace it.
                Directory.Move(temporaryDirectory, rosterDirectory);
            }
            catch (IOException) when (File.Exists(Path.Combine(rosterDirectory, "voters.json")))
            {
                // Another process published the record first.
            }
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                File.Delete(temporaryPath);
                Directory.Delete(temporaryDirectory);
            }
        }
    }
}

using System.Net;

namespace CadenceStudio.Core.Updates;

public static class UpdateDownload
{
    public static async Task<UpdateTransaction> StageAsync(UpdateTransaction t, CancellationToken cancellationToken = default)
    {
        if (t.Manifest is null || t.SyntheticTest || t.State != UpdateTransactionState.Prepared)
            throw new InvalidDataException("Invalid download transaction.");
        var asset = UpdateMaterials.Select(t.Manifest);
        using var tree = new UpdateFileTree();
        tree.Pin(t.StagingRoot);
        var lockPath = Path.Combine(t.StagingRoot, "active.lock");
        UpdateTransactionPaths.Canonical(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        t = UpdateTransactionStore.Read(Path.Combine(t.StagingRoot, "transaction.json"), t.InstallRoot);
        try
        {
            t = UpdateTransactionStore.Transition(t, UpdateTransactionState.Downloading, "Downloading selected release payload and detached signature.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None });
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CadenceStudio/" + ProductInfo.InformationalVersion);
            tree.EnsureParent(t.PayloadPath);
            await Download(client, asset.DownloadUrl, t.PayloadPath, asset.Size, timeout.Token);
            await Download(client, asset.Signature.DownloadUrl, t.SignaturePath, asset.Signature.Size, timeout.Token);
            using (UpdateMaterials.OpenVerified(t)) { }
            return UpdateTransactionStore.Transition(t, UpdateTransactionState.MaterialsVerified,
                "Main application verified exact names, sizes, both SHA-256 hashes and pinned detached signature.");
        }
        catch (Exception e)
        {
            UpdateTransactionStore.Transition(t, UpdateTransactionState.Failed, e.Message);
            throw;
        }
    }

    private static async Task Download(HttpClient client, string url, string destination, long size, CancellationToken token)
    {
        var uri = new Uri(url);
        HttpResponseMessage? response = null;
        try
        {
            for (var hop = 0; hop < 4; hop++)
            {
                response?.Dispose();
                response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) break;
                var location = response.Headers.Location ?? throw new InvalidDataException("Missing redirect location.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                    uri.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new InvalidDataException("Unapproved release download redirect.");
            }
            if (response is null || response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentEncoding.Count != 0 ||
                (response.Content.Headers.ContentLength is long length && length != size))
                throw new InvalidDataException("Unexpected release response or byte size.");
            var headerName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            if (headerName is not null && headerName != Path.GetFileName(destination))
                throw new InvalidDataException("Response filename differs from selected asset.");
            UpdateTransactionPaths.Canonical(destination);
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long count = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, token)) != 0)
            {
                count += read;
                if (count > size) throw new InvalidDataException("Downloaded asset exceeds exact expected size.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            if (count != size) throw new InvalidDataException("Downloaded asset is truncated.");
            output.Flush(true);
        }
        finally { response?.Dispose(); }
    }
}

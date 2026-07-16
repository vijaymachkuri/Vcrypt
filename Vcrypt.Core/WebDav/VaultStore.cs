using System;
using System.Threading.Tasks;
using NWebDav.Server.Http;
using NWebDav.Server.Stores;
using Vcrypt.Core.Services;

namespace Vcrypt.Core.WebDav
{
    public class VaultStore : IStore
    {
        private readonly AesEncryptionProvider _vault;

        public VaultStore(AesEncryptionProvider vault)
        {
            _vault = vault;
        }

        public Task<IStoreItem> GetItemAsync(Uri uri, IHttpContext httpContext)
        {
            // The URI path comes in as e.g. /vault/ or /vault/MyFolder/file.txt
            string path = Uri.UnescapeDataString(uri.LocalPath);
            if (path.StartsWith("/vault", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(6); // remove "/vault"
            }
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                return Task.FromResult<IStoreItem>(new VaultStoreCollection(_vault, ""));
            }

            path = path.TrimStart('/');
            var item = _vault.GetItemByPath(path);
            
            if (item == null)
            {
                return Task.FromResult<IStoreItem>(null);
            }

            if (item.IsFolder)
            {
                return Task.FromResult<IStoreItem>(new VaultStoreCollection(_vault, path));
            }
            else
            {
                return Task.FromResult<IStoreItem>(new VaultStoreItem(_vault, item));
            }
        }

        public Task<IStoreCollection> GetCollectionAsync(Uri uri, IHttpContext httpContext)
        {
            return GetItemAsync(uri, httpContext).ContinueWith(t => t.Result as IStoreCollection);
        }
    }
}

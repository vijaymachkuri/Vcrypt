using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using NWebDav.Server;
using NWebDav.Server.Http;
using NWebDav.Server.Locking;
using NWebDav.Server.Props;
using NWebDav.Server.Stores;
using Vcrypt.Core.Models;
using Vcrypt.Core.Services;

namespace Vcrypt.Core.WebDav
{
    public class VaultStoreCollection : IStoreCollection
    {
        private readonly AesEncryptionProvider _vault;
        private readonly string _path;

        public VaultStoreCollection(AesEncryptionProvider vault, string path)
        {
            _vault = vault;
            _path = path;
            PropertyManager = new PropertyManager<VaultStoreCollection>(new DavProperty<VaultStoreCollection>[]
            {
                new DavGetResourceType<VaultStoreCollection> { Getter = (context, collection) => new[] { XElement.Parse("<D:collection xmlns:D=\"DAV:\"/>") } }
            });
        }

        public string Name => string.IsNullOrEmpty(_path) ? "Vault" : Path.GetFileName(_path);
        public string UniqueKey => _path;
        public IPropertyManager PropertyManager { get; }
        public ILockingManager LockingManager => new InMemoryLockingManager();
        public InfiniteDepthMode InfiniteDepthMode => InfiniteDepthMode.Rejected;

        public Task<IStoreItem> GetItemAsync(string name, IHttpContext httpContext)
        {
            string fullPath = string.IsNullOrEmpty(_path) ? name : $"{_path}/{name}";
            var item = _vault.GetItemByPath(fullPath);
            
            if (item == null) return Task.FromResult<IStoreItem>(null);

            if (item.IsFolder)
                return Task.FromResult<IStoreItem>(new VaultStoreCollection(_vault, fullPath));
            
            return Task.FromResult<IStoreItem>(new VaultStoreItem(_vault, item));
        }

        public Task<IEnumerable<IStoreItem>> GetItemsAsync(IHttpContext httpContext)
        {
            var folder = _vault.GetItemByPath(_path) ?? _vault.CurrentIndex?.Root;
            var items = new List<IStoreItem>();

            if (folder != null)
            {
                foreach (var child in folder.Children)
                {
                    if (child.IsFolder)
                        items.Add(new VaultStoreCollection(_vault, string.IsNullOrEmpty(_path) ? child.Name : $"{_path}/{child.Name}"));
                    else
                        items.Add(new VaultStoreItem(_vault, child));
                }
            }

            return Task.FromResult<IEnumerable<IStoreItem>>(items);
        }

        public async Task<StoreItemResult> CreateItemAsync(string name, bool overwrite, IHttpContext httpContext)
        {
            string fullPath = string.IsNullOrEmpty(_path) ? name : $"{_path}/{name}";
            var folder = _vault.GetItemByPath(_path) ?? _vault.CurrentIndex?.Root;
            
            var existing = folder?.Children.FirstOrDefault(c => c.Name == name);
            if (existing != null && !overwrite)
            {
                return new StoreItemResult(DavStatusCode.PreconditionFailed);
            }
            if (existing != null)
            {
                folder?.Children.Remove(existing);
            }

            var newItem = new EncryptedItemModel
            {
                Name = name,
                IsFolder = false,
                BlobId = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
                Size = 0
            };

            folder?.Children.Add(newItem);
            await _vault.SaveIndexAsync();

            return new StoreItemResult(DavStatusCode.Created, new VaultStoreItem(_vault, newItem));
        }

        public async Task<StoreCollectionResult> CreateCollectionAsync(string name, bool overwrite, IHttpContext httpContext)
        {
            await _vault.CreateFolderAsync(name, _path);
            string fullPath = string.IsNullOrEmpty(_path) ? name : $"{_path}/{name}";
            return new StoreCollectionResult(DavStatusCode.Created, new VaultStoreCollection(_vault, fullPath));
        }

        public async Task<DavStatusCode> DeleteItemAsync(string name, IHttpContext httpContext)
        {
            string fullPath = string.IsNullOrEmpty(_path) ? name : $"{_path}/{name}";
            var item = _vault.GetItemByPath(fullPath);
            if (item == null) return DavStatusCode.NotFound;

            await _vault.DeleteItemAsync(item);
            return DavStatusCode.Ok;
        }

        public Task<StoreItemResult> CopyAsync(IStoreCollection destination, string name, bool overwrite, IHttpContext httpContext) => Task.FromResult(new StoreItemResult(DavStatusCode.NotImplemented));
        public Task<StoreItemResult> MoveItemAsync(string sourceName, IStoreCollection destination, string destinationName, bool overwrite, IHttpContext httpContext) => Task.FromResult(new StoreItemResult(DavStatusCode.NotImplemented));
        public bool SupportsFastMove(IStoreCollection destination, string destinationName, bool overwrite, IHttpContext httpContext) => false;
        
        // IStoreItem implementation on Collection
        public Task<Stream> GetReadableStreamAsync(IHttpContext httpContext) => Task.FromResult<Stream>(null);
        public Task<DavStatusCode> UploadFromStreamAsync(IHttpContext httpContext, Stream source) => Task.FromResult(DavStatusCode.Forbidden);
    }
}

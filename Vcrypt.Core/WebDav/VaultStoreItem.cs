using System;
using System.IO;
using System.Security.Cryptography;
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
    public class VaultStoreItem : IStoreItem
    {
        private readonly AesEncryptionProvider _vault;
        private readonly EncryptedItemModel _item;

        public VaultStoreItem(AesEncryptionProvider vault, EncryptedItemModel item)
        {
            _vault = vault;
            _item = item;
            PropertyManager = new PropertyManager<VaultStoreItem>(new DavProperty<VaultStoreItem>[]
            {
                new DavCreationDate<VaultStoreItem> { Getter = (context, i) => i._item.CreatedAt },
                new DavGetLastModified<VaultStoreItem> { Getter = (context, i) => i._item.CreatedAt },
                new DavGetContentLength<VaultStoreItem> { Getter = (context, i) => i._item.Size },
                new DavGetResourceType<VaultStoreItem> { Getter = (context, i) => null }
            });
        }

        public string Name => _item.Name;
        public string UniqueKey => _item.BlobId;
        public IPropertyManager PropertyManager { get; }
        public ILockingManager LockingManager => new InMemoryLockingManager();

        public async Task<Stream> GetReadableStreamAsync(IHttpContext httpContext)
        {
            if (!_vault.IsUnlocked) throw new Exception("Vault is locked");

            string sourceBlob = Path.Combine(_vault.VaultPath, _item.BlobId + ".vcrypt");
            
            // Open read stream, read IV, and return CryptoStream
            FileStream fsIn = new FileStream(sourceBlob, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.Asynchronous | FileOptions.SequentialScan);
            Aes aes = Aes.Create();
            aes.Key = _vault.EncryptionKey;
            
            byte[] iv = new byte[aes.BlockSize / 8];
            await fsIn.ReadAsync(iv, 0, iv.Length);
            aes.IV = iv;

            CryptoStream cs = new CryptoStream(fsIn, aes.CreateDecryptor(), CryptoStreamMode.Read);
            return cs;
        }

        public async Task<DavStatusCode> UploadFromStreamAsync(IHttpContext httpContext, Stream source)
        {
            if (!_vault.IsUnlocked) return DavStatusCode.Forbidden;

            string destBlob = Path.Combine(_vault.VaultPath, _item.BlobId + ".vcrypt");

            using (Aes aes = Aes.Create())
            {
                aes.Key = _vault.EncryptionKey;
                aes.GenerateIV();

                using (FileStream fsOut = new FileStream(destBlob, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await fsOut.WriteAsync(aes.IV, 0, aes.IV.Length);
                    using (CryptoStream cs = new CryptoStream(fsOut, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        await source.CopyToAsync(cs, 1048576);
                    }
                }
            }

            // Update size in index
            long size = new FileInfo(destBlob).Length - 16; // AES block size is 16 bytes
            _item.Size = size > 0 ? size : 0;
            await _vault.SaveIndexAsync();

            return DavStatusCode.Ok;
        }

        public Task<StoreItemResult> CopyAsync(IStoreCollection destination, string name, bool overwrite, IHttpContext httpContext)
        {
            return Task.FromResult(new StoreItemResult(DavStatusCode.NotImplemented));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Vcrypt.Core.Interfaces;
using Vcrypt.Core.Models;

namespace Vcrypt.Core.Services
{
    public class AesEncryptionProvider : IEncryptionProvider
    {
        private string _vaultPath = string.Empty;
        private byte[] _encryptionKey = Array.Empty<byte>();
        private readonly byte[] _salt = new byte[] { 0x56, 0x63, 0x72, 0x79, 0x70, 0x74, 0x53, 0x61, 0x6c, 0x74 };

        public bool IsUnlocked { get; private set; }
        public VaultIndex? CurrentIndex { get; private set; }
        public string VaultPath => _vaultPath;
        public byte[] EncryptionKey => _encryptionKey;

        public void Initialize(string vaultPath)
        {
            _vaultPath = vaultPath;
            if (!_vaultPath.EndsWith("\\") && !_vaultPath.EndsWith("/"))
            {
                _vaultPath += "\\";
            }
        }

        public async Task InitializeNewVaultAsync(string password)
        {
            DeriveKey(password);
            CurrentIndex = new VaultIndex();
            await SaveIndexAsync();
            IsUnlocked = true;
        }

        public async Task<bool> UnlockAsync(string password)
        {
            DeriveKey(password);
            try
            {
                var indexPath = Path.Combine(_vaultPath, ".index.json");
                if (!File.Exists(indexPath)) return false;

                byte[] encryptedIndex = await File.ReadAllBytesAsync(indexPath);
                byte[] decryptedIndex = DecryptBytes(encryptedIndex);
                
                string json = System.Text.Encoding.UTF8.GetString(decryptedIndex);
                CurrentIndex = JsonSerializer.Deserialize<VaultIndex>(json);
                IsUnlocked = true;
                return true;
            }
            catch
            {
                IsUnlocked = false;
                return false;
            }
        }

        public void Lock()
        {
            IsUnlocked = false;
            CurrentIndex = null;
            Array.Clear(_encryptionKey, 0, _encryptionKey.Length);
        }

        private void DeriveKey(string password)
        {
            using (var deriveBytes = new Rfc2898DeriveBytes(password, _salt, 100000, HashAlgorithmName.SHA256))
            {
                _encryptionKey = deriveBytes.GetBytes(32); // 256 bits
            }
        }

        public async Task SaveIndexAsync()
        {
            if (CurrentIndex == null) return;
            string json = JsonSerializer.Serialize(CurrentIndex);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            byte[] encrypted = EncryptBytes(bytes);
            await File.WriteAllBytesAsync(Path.Combine(_vaultPath, ".index.json"), encrypted);
        }

        private byte[] EncryptBytes(byte[] clearText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = _encryptionKey;
                aes.GenerateIV();
                using (MemoryStream ms = new MemoryStream())
                {
                    ms.Write(aes.IV, 0, aes.IV.Length);
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(clearText, 0, clearText.Length);
                        cs.FlushFinalBlock();
                    }
                    return ms.ToArray();
                }
            }
        }

        private byte[] DecryptBytes(byte[] cipherText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = _encryptionKey;
                byte[] iv = new byte[aes.BlockSize / 8];
                Array.Copy(cipherText, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(new MemoryStream(cipherText, iv.Length, cipherText.Length - iv.Length), aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        cs.CopyTo(ms);
                    }
                    return ms.ToArray();
                }
            }
        }

        public async Task EncryptFileAsync(string sourcePath, string targetParentPath = "")
        {
            if (!IsUnlocked || CurrentIndex == null) throw new Exception("Vault is locked");

            string blobId = Guid.NewGuid().ToString("N");
            string destPath = Path.Combine(_vaultPath, blobId + ".vcrypt");

            using (Aes aes = Aes.Create())
            {
                aes.Key = _encryptionKey;
                aes.GenerateIV();

                using (FileStream fsOut = new FileStream(destPath, FileMode.Create))
                {
                    fsOut.Write(aes.IV, 0, aes.IV.Length);
                    using (CryptoStream cs = new CryptoStream(fsOut, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    using (FileStream fsIn = new FileStream(sourcePath, FileMode.Open, FileAccess.Read))
                    {
                        await fsIn.CopyToAsync(cs);
                    }
                }
            }

            var item = new EncryptedItemModel
            {
                Name = Path.GetFileName(sourcePath),
                IsFolder = false,
                Size = new FileInfo(sourcePath).Length,
                BlobId = blobId
            };

            var parent = FindFolder(CurrentIndex.Root, targetParentPath) ?? CurrentIndex.Root;
            parent.Children.Add(item);
            await SaveIndexAsync();
        }

        public async Task DecryptFileAsync(EncryptedItemModel file, string destinationDirectory)
        {
            if (!IsUnlocked) throw new Exception("Vault is locked");

            string sourceBlob = Path.Combine(_vaultPath, file.BlobId + ".vcrypt");
            string destPath = Path.Combine(destinationDirectory, file.Name);

            using (Aes aes = Aes.Create())
            {
                aes.Key = _encryptionKey;
                using (FileStream fsIn = new FileStream(sourceBlob, FileMode.Open, FileAccess.Read))
                {
                    byte[] iv = new byte[aes.BlockSize / 8];
                    await fsIn.ReadAsync(iv, 0, iv.Length);
                    aes.IV = iv;

                    using (CryptoStream cs = new CryptoStream(fsIn, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    using (FileStream fsOut = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                    {
                        await cs.CopyToAsync(fsOut);
                    }
                }
            }
        }

        public async Task CreateFolderAsync(string folderName, string parentPath = "")
        {
            if (!IsUnlocked || CurrentIndex == null) return;

            var parent = FindFolder(CurrentIndex.Root, parentPath) ?? CurrentIndex.Root;
            parent.Children.Add(new EncryptedItemModel
            {
                Name = folderName,
                IsFolder = true
            });
            await SaveIndexAsync();
        }

        public async Task DeleteItemAsync(EncryptedItemModel item)
        {
            if (!IsUnlocked || CurrentIndex == null) return;
            
            // recursive delete blob files
            void DeleteBlobs(EncryptedItemModel node)
            {
                if (!node.IsFolder && !string.IsNullOrEmpty(node.BlobId))
                {
                    string p = Path.Combine(_vaultPath, node.BlobId + ".vcrypt");
                    if (File.Exists(p)) File.Delete(p);
                }
                foreach (var child in node.Children) DeleteBlobs(child);
            }
            
            DeleteBlobs(item);
            
            RemoveItemFromTree(CurrentIndex.Root, item);
            await SaveIndexAsync();
        }

        private EncryptedItemModel? FindFolder(EncryptedItemModel current, string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath)) return current;
            var parts = targetPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            var ptr = current;
            foreach (var part in parts)
            {
                var next = ptr.Children.FirstOrDefault(c => c.IsFolder && c.Name == part);
                if (next == null) return null;
                ptr = next;
            }
            return ptr;
        }

        public EncryptedItemModel? GetItemByPath(string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath)) return CurrentIndex?.Root;
            
            var parts = targetPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var ptr = CurrentIndex?.Root;
            
            for (int i = 0; i < parts.Length; i++)
            {
                if (ptr == null) return null;
                var next = ptr.Children.FirstOrDefault(c => c.Name == parts[i]);
                if (next == null) return null;
                ptr = next;
            }
            
            return ptr;
        }

        private bool RemoveItemFromTree(EncryptedItemModel parent, EncryptedItemModel item)
        {
            if (parent.Children.Remove(item)) return true;
            foreach (var child in parent.Children.Where(c => c.IsFolder))
            {
                if (RemoveItemFromTree(child, item)) return true;
            }
            return false;
        }
    }
}

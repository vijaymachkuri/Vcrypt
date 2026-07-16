using Vcrypt.Core.Interfaces;
using Vcrypt.Core.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Vcrypt.Core.Services
{
    public class AesEncryptionProvider : IEncryptionProvider
    {
        private const int KEY_SIZE = 32;
        private const int IV_SIZE = 16;
        private const int SALT_SIZE = 16;
        private const int ITERATIONS = 100_000;

        private string _vaultPath = string.Empty;
        private string _indexPath = string.Empty;
        private byte[]? _key;

        public VaultIndex? CurrentIndex { get; private set; }
        public bool IsUnlocked => _key != null;

        public void Initialize(string vaultPath)
        {
            _vaultPath = vaultPath;
            _indexPath = Path.Combine(_vaultPath, "index.enc");
        }

        private byte[] DeriveKey(string password, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, ITERATIONS, HashAlgorithmName.SHA256, KEY_SIZE);
        }

        public async Task InitializeNewVaultAsync(string password)
        {
            var salt = new byte[SALT_SIZE];
            RandomNumberGenerator.Fill(salt);
            _key = DeriveKey(password, salt);
            CurrentIndex = new VaultIndex();
            await SaveIndexAsync(salt);
        }

        public async Task<bool> UnlockAsync(string password)
        {
            if (!File.Exists(_indexPath)) return false;

            try
            {
                using var fs = new FileStream(_indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var salt = new byte[SALT_SIZE];
                var iv = new byte[IV_SIZE];
                int saltRead = await fs.ReadAsync(salt, 0, SALT_SIZE);
                int ivRead = await fs.ReadAsync(iv, 0, IV_SIZE);
                
                if (saltRead != SALT_SIZE || ivRead != IV_SIZE) return false;

                var tempKey = DeriveKey(password, salt);

                using var aes = Aes.Create();
                aes.Key = tempKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;

                using var cs = new CryptoStream(fs, aes.CreateDecryptor(), CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);
                var json = await sr.ReadToEndAsync();

                CurrentIndex = JsonSerializer.Deserialize<VaultIndex>(json);
                _key = tempKey; 
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Lock()
        {
            _key = null;
            CurrentIndex = null;
        }

        private async Task SaveIndexAsync(byte[]? existingSalt = null)
        {
            if (!IsUnlocked || _key == null) throw new InvalidOperationException("Vault is locked.");

            byte[] salt = existingSalt ?? new byte[SALT_SIZE];
            if (existingSalt == null && File.Exists(_indexPath))
            {
                using var fsRead = new FileStream(_indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await fsRead.ReadAsync(salt, 0, SALT_SIZE);
            }

            var iv = new byte[IV_SIZE];
            RandomNumberGenerator.Fill(iv);

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;

            using var fs = new FileStream(_indexPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await fs.WriteAsync(salt, 0, SALT_SIZE);
            await fs.WriteAsync(iv, 0, IV_SIZE);

            using var cs = new CryptoStream(fs, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using var sw = new StreamWriter(cs);
            var json = JsonSerializer.Serialize(CurrentIndex);
            await sw.WriteAsync(json);
        }

        public async Task EncryptFileAsync(string sourcePath)
        {
            if (!IsUnlocked || _key == null || CurrentIndex == null) throw new InvalidOperationException("Vault is locked.");

            var fileInfo = new FileInfo(sourcePath);
            var blobId = Guid.NewGuid().ToString("N");
            var outPath = Path.Combine(_vaultPath, blobId);

            var iv = new byte[IV_SIZE];
            RandomNumberGenerator.Fill(iv);

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;

            using (var fsOut = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await fsOut.WriteAsync(iv, 0, IV_SIZE);
                using var cs = new CryptoStream(fsOut, aes.CreateEncryptor(), CryptoStreamMode.Write);
                using var fsIn = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                
                await fsIn.CopyToAsync(cs, 81920); 
            }

            CurrentIndex.Files.Add(new EncryptedFileModel
            {
                OriginalName = fileInfo.Name,
                Size = fileInfo.Length,
                BlobId = blobId
            });

            await SaveIndexAsync();
        }
    }
}

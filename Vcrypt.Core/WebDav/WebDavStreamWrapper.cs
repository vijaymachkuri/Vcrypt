using System;
using System.IO;
using System.Security.Cryptography;

namespace Vcrypt.Core.WebDav
{
    public class WebDavStreamWrapper : Stream
    {
        private readonly string _filePath;
        private readonly byte[] _key;
        private readonly long _length;

        private FileStream? _fileStream;
        private CryptoStream? _cryptoStream;
        private Aes? _aes;
        private long _position;

        public WebDavStreamWrapper(string filePath, byte[] key, long length)
        {
            _filePath = filePath;
            _key = key;
            _length = length;
            _position = 0;

            InitializeStream();
        }

        private void InitializeStream()
        {
            _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.Asynchronous | FileOptions.SequentialScan);
            _aes = Aes.Create();
            _aes.Key = _key;

            byte[] iv = new byte[_aes.BlockSize / 8];
            int readBytes = _fileStream.Read(iv, 0, iv.Length);
            if (readBytes < iv.Length)
            {
                throw new CryptographicException("Failed to read IV from the encrypted file.");
            }
            _aes.IV = iv;

            _cryptoStream = new CryptoStream(_fileStream, _aes.CreateDecryptor(), CryptoStreamMode.Read);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() => _cryptoStream?.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_cryptoStream == null) throw new ObjectDisposedException(nameof(WebDavStreamWrapper));
            if (_position >= _length) return 0;
            
            int toRead = (int)Math.Min(count, _length - _position);
            if (toRead <= 0) return 0;

            int read = _cryptoStream.Read(buffer, offset, toRead);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long targetPosition = offset;
            if (origin == SeekOrigin.Current) targetPosition = _position + offset;
            else if (origin == SeekOrigin.End) targetPosition = _length + offset;

            if (targetPosition < 0) targetPosition = 0;
            if (targetPosition > _length) targetPosition = _length;

            if (targetPosition < _position)
            {
                // Reset and start from the beginning
                _cryptoStream?.Dispose();
                _fileStream?.Dispose();
                _aes?.Dispose();

                InitializeStream();
                _position = 0;
            }

            if (targetPosition > _position && _cryptoStream != null)
            {
                long bytesToSkip = targetPosition - _position;
                byte[] junk = new byte[8192];
                while (bytesToSkip > 0)
                {
                    int toRead = (int)Math.Min(bytesToSkip, junk.Length);
                    int read = _cryptoStream.Read(junk, 0, toRead);
                    if (read == 0) break; // EOF
                    _position += read;
                    bytesToSkip -= read;
                }
            }

            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cryptoStream?.Dispose();
                _fileStream?.Dispose();
                _aes?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

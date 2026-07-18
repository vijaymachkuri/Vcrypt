using System;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Vcrypt.Core.Interfaces;
using LLama;
using LLama.Common;

namespace Vcrypt.Infrastructure.AI
{
    public class LocalAiService : ILocalAiService, IDisposable
    {
        private const string MODEL_URL = "https://huggingface.co/vijaymachkuri/gem-AI/resolve/main/Phi-3-mini-4k-instruct-q4.gguf?download=true";
        private const string MODEL_FILE_NAME = "Phi-3-mini-4k-instruct-q4.gguf";
        private readonly string _modelDirectory;
        private readonly string _modelPath;

        private LLamaWeights? _weights;
        private LLamaContext? _context;
        private InteractiveExecutor? _executor;
        private ChatSession? _chatSession;

        public LocalAiService()
        {
            // Store the model in AppData/Local/Vcrypt/Models
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _modelDirectory = Path.Combine(localAppData, "Vcrypt", "Models");
            _modelPath = Path.Combine(_modelDirectory, MODEL_FILE_NAME);
        }

        // 1. Hardware and Environment Checks
        public bool MeetsHardwareRequirements()
        {
            // Get total system RAM available to the garbage collector
            long totalRamBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            double totalRamGb = totalRamBytes / (1024.0 * 1024.0 * 1024.0);

            // Phi-3 Mini Q4 needs ~3-4GB of RAM. We require the system to have at least 8GB total to run smoothly.
            return totalRamGb >= 7.5; // Using 7.5 to account for hardware reservations
        }

        public bool IsModelDownloaded()
        {
            return File.Exists(_modelPath);
        }

        // 2. Download Management
        public async Task DownloadModelAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (IsModelDownloaded()) return;

            Directory.CreateDirectory(_modelDirectory);
            
            // Download to a temporary file first so we don't end up with corrupted/partial downloads if cancelled
            string tempPath = _modelPath + ".tmp";

            using var httpClient = new HttpClient();
            // Hugging Face blocks automated downloads that don't have a User-Agent
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Vcrypt-Desktop-App/1.0");
            
            using var response = await httpClient.GetAsync(MODEL_URL, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? 1;
            long totalRead = 0;
            // Increase buffer to 1MB to drastically improve download speed and reduce async overhead
            byte[] buffer = new byte[1048576];
            bool isMoreToRead = true;

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, true);

            double lastReportedPercent = -1;

            while (isMoreToRead)
            {
                int read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read == 0)
                {
                    isMoreToRead = false;
                }
                else
                {
                    await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                    totalRead += read;
                    double percent = (double)totalRead / totalBytes * 100;
                    
                    // Only update the UI if the percentage has grown by at least 0.1% 
                    // Otherwise, we flood the UI with 30,000 messages a second and freeze the app/download!
                    if (percent - lastReportedPercent >= 0.1)
                    {
                        progress?.Report(percent);
                        lastReportedPercent = percent;
                    }
                }
            }

            // Successfully downloaded, rename temp to real
            fileStream.Close();
            File.Move(tempPath, _modelPath, overwrite: true);
        }

        // 3. AI Interaction
        public Task InitializeAsync(string systemPrompt = "")
        {
            if (!IsModelDownloaded())
                throw new FileNotFoundException("Model file not found. Please download it first.");

            if (_weights != null) return Task.CompletedTask; // Already initialized

            var parameters = new ModelParams(_modelPath)
            {
                ContextSize = 2048, // Limit context size to save RAM
                GpuLayerCount = 0 // Run purely on CPU for maximum compatibility
            };

            // Load weights into memory
            _weights = LLamaWeights.LoadFromFile(parameters);
            _context = _weights.CreateContext(parameters);
            
            _executor = new InteractiveExecutor(_context);
            
            // Set up a ChatSession with the Persona
            var history = new ChatHistory();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                history.AddMessage(AuthorRole.System, systemPrompt);
            }
            
            // _chatSession = new ChatSession(_executor, history);

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> SendMessageStreamAsync(string message, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (_executor == null)
                throw new InvalidOperationException("AI Model is not initialized.");

            // Create inference parameters (controls how creative the AI is)
            var inferenceParams = new InferenceParams()
            {
                AntiPrompts = new List<string> { "<|end|>", "<|user|>" }
            };

            // Stream the tokens as they are generated
            await foreach (var text in _executor.InferAsync(message, inferenceParams, cancellationToken))
            {
                yield return text;
            }
        }

        // 4. Memory Management
        public void UnloadModel()
        {
            _executor = null;
            _chatSession = null;
            
            _context?.Dispose();
            _context = null;

            _weights?.Dispose();
            _weights = null;

            // Force garbage collection to free up the RAM immediately
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void Dispose()
        {
            UnloadModel();
        }
    }
}

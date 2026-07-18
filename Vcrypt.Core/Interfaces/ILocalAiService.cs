using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Vcrypt.Core.Interfaces
{
    public interface ILocalAiService
    {
        // 1. Hardware and Environment Checks
        bool MeetsHardwareRequirements();
        bool IsModelDownloaded();
        
        // 2. Download Management
        Task DownloadModelAsync(IProgress<double> progress, CancellationToken cancellationToken);
        
        // 3. AI Interaction
        Task InitializeAsync(string systemPrompt = "");
        IAsyncEnumerable<string> SendMessageStreamAsync(string message, CancellationToken cancellationToken);
        
        // 4. Memory Management
        void UnloadModel();
    }
}

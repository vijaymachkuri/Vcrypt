using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NWebDav.Server;
using NWebDav.Server.HttpListener;
using Vcrypt.Core.Services;

namespace Vcrypt.Core.WebDav
{
    public class WebDavServerManager
    {
        private HttpListener _httpListener;
        private CancellationTokenSource _cts;
        private Task _dispatchTask;
        
        public string BaseUrl { get; } = "http://localhost:13374/";

        public void Start(AesEncryptionProvider vault)
        {
            if (_httpListener != null) return;

            var store = new VaultStore(vault);
            var dispatcher = new WebDavDispatcher(store, new RequestHandlerFactory());

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(BaseUrl);
            _httpListener.Start();

            _cts = new CancellationTokenSource();
            _dispatchTask = Task.Run(() => DispatchLoop(dispatcher, _cts.Token), _cts.Token);
            
            Debug.WriteLine($"WebDAV Server started at {BaseUrl}");
        }

        private async Task DispatchLoop(WebDavDispatcher dispatcher, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var httpListenerContext = await _httpListener.GetContextAsync();
                    
                    // Process request concurrently to prevent blocking other WebDAV operations
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            var nwebDavContext = new HttpContext(httpListenerContext);
                            await dispatcher.DispatchRequestAsync(nwebDavContext);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"WebDAV Request Error: {ex.Message}");
                        }
                    });
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped or closed
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WebDAV Dispatch Error: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            if (_httpListener == null) return;

            _cts?.Cancel();
            _httpListener.Stop();
            _httpListener.Close();
            
            try { _dispatchTask?.Wait(1000); } catch { }

            _httpListener = null;
            _cts = null;
            _dispatchTask = null;
            
            Debug.WriteLine("WebDAV Server stopped");
        }
    }
}

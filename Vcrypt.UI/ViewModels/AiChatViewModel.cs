using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vcrypt.Core.Interfaces;

namespace Vcrypt.UI.ViewModels
{
    public partial class ChatMessage : ObservableObject
    {
        [ObservableProperty]
        private string _sender = "";

        [ObservableProperty]
        private string _text = "";

        [ObservableProperty]
        private bool _isAi = false;
    }

    public partial class AiChatViewModel : ObservableObject
    {
        private readonly ILocalAiService _aiService;
        private readonly MainWindowViewModel _mainVm;
        private CancellationTokenSource? _downloadCts;

        private const string SystemPrompt = @"You are Gem, a highly secure, offline Virtual Assistant built into the Vcrypt software. 
Your primary job is to help the user manage their secure vault, encrypt files, and understand the app's features.
You have the ability to perform actions on behalf of the user. If the user asks you to do something, you MUST output an action tag at the EXACT END of your response.
Available actions:
- [ACTION: LOCK_VAULT] -> Locks the secure vault.
- [ACTION: ADD_FILES] -> Opens the file picker to encrypt files.
- [ACTION: ADD_FOLDERS] -> Opens the folder picker to encrypt folders.
- [ACTION: FIND_DUPLICATES] -> Opens the duplicate scanner.
- [ACTION: FIND_EMPTY_FOLDERS] -> Opens the empty folder scanner.
- [ACTION: OPEN_EXPLORER] -> Opens Windows Explorer to view the Z: drive.

For example, if the user says 'Lock my vault', you should say 'I will lock your vault now. [ACTION: LOCK_VAULT]'.";

        [ObservableProperty]
        private string _currentView = "Loading"; // "Loading", "Download", "Chat"

        [ObservableProperty]
        private double _downloadProgress = 0;

        [ObservableProperty]
        private string _downloadStatus = "Ready to download Phi-3 Mini (2.4 GB)";

        [ObservableProperty]
        private bool _isDownloading = false;

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        [ObservableProperty]
        private string _inputText = "";

        [ObservableProperty]
        private bool _isChatting = false; // true if AI is currently generating

        private bool _isFirstMessage = true;

        public AiChatViewModel(ILocalAiService aiService, MainWindowViewModel mainVm)
        {
            _aiService = aiService;
            _mainVm = mainVm;
        }

        public async Task InitializeAsync()
        {
            if (_aiService.IsModelDownloaded())
            {
                CurrentView = "Loading";
                await StartAiEngineAsync();
            }
            else
            {
                CurrentView = "Download";
            }
        }

        [RelayCommand]
        private async Task DownloadModel()
        {
            IsDownloading = true;
            DownloadStatus = "Downloading model (this may take a while)...";
            _downloadCts = new CancellationTokenSource();

            var progress = new Progress<double>(percent =>
            {
                DownloadProgress = percent;
            });

            try
            {
                await Task.Run(() => _aiService.DownloadModelAsync(progress, _downloadCts.Token));
                DownloadStatus = "Download complete. Initializing engine...";
                await StartAiEngineAsync();
            }
            catch (OperationCanceledException)
            {
                DownloadStatus = "Download cancelled.";
            }
            catch (Exception ex)
            {
                DownloadStatus = $"Download failed: {ex.Message}";
            }
            finally
            {
                IsDownloading = false;
            }
        }

        [RelayCommand]
        private void CancelDownload()
        {
            _downloadCts?.Cancel();
        }

        private async Task StartAiEngineAsync()
        {
            try
            {
                await Task.Run(() => _aiService.InitializeAsync());
                
                _isFirstMessage = true;
                
                // Add a welcome message
                Messages.Clear();
                Messages.Add(new ChatMessage { Sender = "Gem", Text = "Hello! I am Gem, your completely private, offline AI assistant. How can I help you today?", IsAi = true });
                
                CurrentView = "Chat";
            }
            catch (Exception ex)
            {
                CurrentView = "Download";
                DownloadStatus = $"Failed to initialize AI: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(InputText) || IsChatting) return;

            string message = InputText;
            InputText = "";
            IsChatting = true;

            // Add user message
            Messages.Add(new ChatMessage { Sender = "You", Text = message, IsAi = false });

            // Add empty AI message placeholder
            var aiMessage = new ChatMessage { Sender = "Gem", Text = "", IsAi = true };
            Messages.Add(aiMessage);

            try
            {
                string promptToSend = "";
                string hiddenReminder = "\n[System Reminder: If you are performing an action, you MUST output the exact [ACTION: ...] tag at the very end of your response.]";
                string messageWithReminder = message + hiddenReminder;

                if (_isFirstMessage)
                {
                    promptToSend = $"<|system|>\n{SystemPrompt}<|end|>\n";
                    promptToSend += $"<|user|>\n{messageWithReminder}<|end|>\n<|assistant|>\n";
                    _isFirstMessage = false;
                }
                else
                {
                    // Prepend <|end|> because the previous assistant generation stopped AT the anti-prompt but didn't include it in context
                    promptToSend = $"<|end|>\n<|user|>\n{messageWithReminder}<|end|>\n<|assistant|>\n";
                }

                string fullResponse = "";
                var cts = new CancellationTokenSource();
                await foreach (var text in _aiService.SendMessageStreamAsync(promptToSend, cts.Token))
                {
                    fullResponse += text;
                    string display = System.Text.RegularExpressions.Regex.Replace(fullResponse, @"\[ACTION:.*?\]", "");
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        aiMessage.Text = display;
                    });
                }

                // After generation is complete, parse for intent using the raw response
                ParseIntentAndExecute(fullResponse);
            }
            catch (Exception ex)
            {
                aiMessage.Text += $"\n[Error: {ex.Message}]";
            }
            finally
            {
                IsChatting = false;
            }
        }

        private void ParseIntentAndExecute(string response)
        {
            if (response.Contains("[ACTION: LOCK_VAULT]"))
            {
                Application.Current.Dispatcher.Invoke(() => _mainVm.LockVaultCommand.Execute(null));
            }
            else if (response.Contains("[ACTION: ADD_FILES]"))
            {
                Application.Current.Dispatcher.Invoke(() => _mainVm.AddFilesCommand.Execute(null));
            }
            else if (response.Contains("[ACTION: ADD_FOLDERS]"))
            {
                Application.Current.Dispatcher.Invoke(() => _mainVm.AddFoldersCommand.Execute(null));
            }
            else if (response.Contains("[ACTION: FIND_DUPLICATES]"))
            {
                Application.Current.Dispatcher.Invoke(() => _mainVm.FindDuplicatesCommand.Execute(null));
            }
            else if (response.Contains("[ACTION: FIND_EMPTY_FOLDERS]"))
            {
                Application.Current.Dispatcher.Invoke(() => _mainVm.FindEmptyFoldersCommand.Execute(null));
            }
            else if (response.Contains("[ACTION: OPEN_EXPLORER]"))
            {
                Application.Current.Dispatcher.Invoke(() => _mainVm.OpenExplorerCommand.Execute(null));
            }
        }

        public void Cleanup()
        {
            _aiService.UnloadModel();
        }
    }
}

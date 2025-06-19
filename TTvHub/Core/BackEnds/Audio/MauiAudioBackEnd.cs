using Plugin.Maui.Audio;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TTvHub.Core.Logs;
using Logger = TTvHub.Core.Logs.StaticLogger;

namespace TTvHub.Core.BackEnds.Audio;

public sealed class MauiAudioBackEnd : IAsyncDisposable
{
    public static string BackEndName => "MauiAudioBackEnd";

    // --- Зависимости, внедряемые через DI ---
    private readonly IAudioManager _audioManager;
    private readonly ITextToSpeech _textToSpeech;

    // --- Состояние ---
    private IAudioPlayer? _currentPlayer;
    public bool IsPlaying => _currentPlayer?.IsPlaying ?? false;
    public string CurrentPlayingAction { get; private set; } = string.Empty;
    public int Volume { get; set; } = 100;
    public float Pitch { get; set; } = 1.0f;

    // --- Очередь на основе Channels ---
    private readonly Channel<(Func<CancellationToken, Task> Action, int Volume)> _channel;
    private readonly CancellationTokenSource _serviceCts = new();
    private CancellationTokenSource? _currentActionCts;

    // --- Настройки ---
    private const string BannedWordsFileName = "badwords.json";
    private const string CensoredPlaceholder = "[filtered]";
    private readonly HashSet<string> _bannedWords;

    private static readonly HttpClient _httpClient = new();

    public MauiAudioBackEnd(IAudioManager audioManager, ITextToSpeech textToSpeech)
    {
        _audioManager = audioManager;
        _textToSpeech = textToSpeech;
        _bannedWords = LoadBannedWords();
        _channel = Channel.CreateUnbounded<(Func<CancellationToken, Task>, int)>();
        Task.Run(() => ProcessChannelAsync(_serviceCts.Token));
    }

    // --- Публичное API ---

    public void VoiceText(string text)
    {
        var filteredText = FilterText(text);
        if (string.IsNullOrWhiteSpace(filteredText))
        {
            Logger.Log(LogCategory.Info, "Text is empty after filtering, skipping TTS.", this);
            return;
        }

        Func<CancellationToken, Task> action = async (token) =>
        {
            CurrentPlayingAction = $"Speaking: {filteredText[..Math.Min(filteredText.Length, 20)]}...";
            Logger.Log(LogCategory.Info, $"Executing TTS for: \"{filteredText}\"", this);

            var speechOptions = new SpeechOptions
            {
                Volume = Math.Clamp(Volume / 100f, 0, 1.0f),
                Pitch = Math.Clamp(Pitch, 0.0f, 2.0f)
            };

            await _textToSpeech.SpeakAsync(filteredText, speechOptions, token);
        };
        _channel.Writer.TryWrite((action, Volume));
    }

    public void PlaySound(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;

        Func<CancellationToken, Task> action = async (token) =>
        {
            CurrentPlayingAction = $"Playing: {Path.GetFileName(uri)}";
            Logger.Log(LogCategory.Info, $"Attempting to play sound from: {uri} at volume {Volume}%", this);

        try
        {
                // --- НОВАЯ, БОЛЕЕ НАДЕЖНАЯ ЛОГИКА ---
                if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // 1. Скачиваем аудиофайл в память
                    Logger.Log(LogCategory.Info, "Downloading network audio stream...", this);
                    using var httpStream = await _httpClient.GetStreamAsync(uri, token);
                    using var memoryStream = new MemoryStream();
                    await httpStream.CopyToAsync(memoryStream, token);
                    memoryStream.Position = 0; // Возвращаем указатель в начало потока

                    // 2. Создаем плеер из потока в памяти
                    _currentPlayer = _audioManager.CreatePlayer(memoryStream);
                }
                else
                {
                    // Воспроизведение из ресурсов приложения (остается без изменений)
                    var stream = await FileSystem.OpenAppPackageFileAsync(uri);
                    _currentPlayer = _audioManager.CreatePlayer(stream);
                }
                // ------------------------------------

                if (_currentPlayer == null)
                {
                    Logger.Log(LogCategory.Error, $"Failed to create player for: {uri}", this);
                    return;
                }

                _currentPlayer.Volume = Math.Clamp(Volume / 100.0, 0, 1.0);

                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = token.Register(() => tcs.TrySetCanceled());

                // Подписываемся на событие до вызова Play()
                void OnPlaybackEnded(object? s, EventArgs e)
                {
                    // Отписываемся, чтобы избежать утечек памяти
                    _currentPlayer.PlaybackEnded -= OnPlaybackEnded;
                    tcs.TrySetResult();
                }
                _currentPlayer.PlaybackEnded += OnPlaybackEnded;

                _currentPlayer.Play();
                await tcs.Task; // Ждем завершения воспроизведения или отмены
            }
        catch (OperationCanceledException)
        {
                // Это ожидаемое исключение при пропуске, его не нужно логировать как ошибку
                Logger.Log(LogCategory.Info, $"Playback cancelled for: {uri}", this);
                throw; // Пробрасываем дальше, чтобы finally-блок сработал корректно
            }
        catch (Exception ex)
        {
                Logger.Log(LogCategory.Error, $"Failed to play sound from '{uri}'", this, ex);
            }
        };

        _channel.Writer.TryWrite((action, Volume));
    }

    public void Skip()
    {
        if (_currentActionCts != null && !_currentActionCts.IsCancellationRequested)
        {
            Logger.Log(LogCategory.Info, $"Skipping current action: {CurrentPlayingAction}", this);
            _currentActionCts.Cancel();
        }
        else
        {
            Logger.Log(LogCategory.Info, "Nothing to skip.", this);
        }
    }

    // --- Внутренняя логика ---

    private async Task ProcessChannelAsync(CancellationToken serviceToken)
    {
        await foreach (var (action, _) in _channel.Reader.ReadAllAsync(serviceToken))
        {
            _currentActionCts = new CancellationTokenSource();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serviceToken, _currentActionCts.Token);
            try
            {
                await action(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.Log(LogCategory.Info, "Audio action was cancelled.", this);
            }
            catch (Exception ex)
            {
                Logger.Log(LogCategory.Error, $"Error executing audio action: {CurrentPlayingAction}", this, ex);
            }
            finally
            {
                _currentPlayer?.Dispose();
                _currentPlayer = null;
                CurrentPlayingAction = string.Empty;
                _currentActionCts.Dispose();
                _currentActionCts = null;
                linkedCts.Dispose();
            }
        }
    }

    private HashSet<string> LoadBannedWords()
    {
        // Для MAUI лучше использовать FileSystem.AppDataDirectory
        var directory = FileSystem.AppDataDirectory;
        var filePath = Path.Combine(directory, BannedWordsFileName);

        if (!File.Exists(filePath))
        {
            Logger.Log(LogCategory.Warning, $"Banned words file not found at '{filePath}'. Generating example.", this);
            try
            {
                var exampleWords = new List<string> { "badword1", "example" };
                File.WriteAllText(filePath, JsonSerializer.Serialize(exampleWords));
            }
            catch (Exception ex)
            {
                Logger.Log(LogCategory.Error, "Failed to generate example banned words file.", this, ex);
            }
            return [];
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var words = JsonSerializer.Deserialize<List<string>>(json);
            var wordSet = words?.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToHashSet() ?? new HashSet<string>();
            Logger.Log(LogCategory.Info, $"Loaded {wordSet.Count} banned words.", this);
            return wordSet;
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, "Failed to load or parse banned words file.", this, ex);
            return [];
        }
    }

    private string FilterText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _bannedWords.Count == 0) return text;

        var tempText = text;
        foreach (var word in _bannedWords)
        {
            // Используем Regex для поиска целых слов, игнорируя регистр
            tempText = Regex.Replace(tempText, $@"\b{Regex.Escape(word)}\b", CensoredPlaceholder, RegexOptions.IgnoreCase);
        }
        return tempText;
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();
        _serviceCts.Cancel();
        _currentPlayer?.Dispose();
    }
}
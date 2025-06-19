using Plugin.Maui.Audio;
using TTvHub.Core.BackEnds.Audio;

namespace TTvHub.Core.BackEnds.Abstractions;

public static class Audio
{
    private static readonly MauiAudioBackEnd audio = new(AudioManager.Current, TextToSpeech.Default);

    public static void PlaySound(string uri)
    {
        if (string.IsNullOrEmpty(uri)) throw new ArgumentNullException(nameof(uri));
        audio.PlaySound(uri);
    }

    public static void PlayText(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentNullException(nameof(text));
        audio.VoiceText(text);
    }

    public static void SkipSound() => audio.Skip();

    // TODO: Rework
    public static void SetVolume(int volume) => audio.Volume = volume;

    public static int GetVolume() => audio.Volume;

    public static void IncreaseVolume(int volume) => audio.Volume += volume;

    public static void DecreaseVolume(int volume) => audio.Volume -= volume;
}
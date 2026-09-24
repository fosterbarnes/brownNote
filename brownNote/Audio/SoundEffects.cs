using System.IO;
using System.Windows.Media;

namespace brownNote.Audio;

public sealed class SoundEffects
{
    private readonly MediaPlayer _buttonDown = Load("radioButtonDown.wav");
    private readonly MediaPlayer _buttonUp = Load("radioButtonUp.wav");
    private readonly MediaPlayer _buttonMechanism = Load("radioButtonMechanism.wav");

    public void ButtonDown() => Replay(_buttonDown);

    public void ButtonUp() => Replay(_buttonUp);

    public void ButtonMechanism() => Replay(_buttonMechanism);

    private static MediaPlayer Load(string fileName)
    {
        var player = new MediaPlayer();
        player.Open(new Uri(Path.Combine(AppContext.BaseDirectory, "Sounds", fileName)));
        return player;
    }

    private static void Replay(MediaPlayer player)
    {
        player.Stop();
        player.Position = TimeSpan.Zero;
        player.Play();
    }
}

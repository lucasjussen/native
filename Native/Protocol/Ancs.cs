using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Native.Protocol;

// Apple Notification Center Service (ANCS) e Apple Media Service (AMS): os dois serviços
// Bluetooth LE que o iPhone oferece a acessórios. Aqui fica só a parte de bytes, testável.

public enum AncsCategory : byte
{
    Other = 0, IncomingCall = 1, MissedCall = 2, Voicemail = 3, Social = 4, Schedule = 5,
    Email = 6, News = 7, HealthAndFitness = 8, BusinessAndFinance = 9, Location = 10, Entertainment = 11
}

public enum AncsEventKind : byte { Added = 0, Modified = 1, Removed = 2 }

public readonly record struct AncsEvent(AncsEventKind Kind, byte Flags, AncsCategory Category, uint Uid)
{
    public bool IsSilent => (Flags & 0x01) != 0;
    public bool IsImportant => (Flags & 0x02) != 0;
    public bool IsPreExisting => (Flags & 0x04) != 0;
    public bool HasPositiveAction => (Flags & 0x08) != 0;
    public bool HasNegativeAction => (Flags & 0x10) != 0;

    public static bool TryParse(byte[] data, out AncsEvent parsed)
    {
        parsed = default;
        if (data.Length < 8 || data[0] > 2) return false;
        parsed = new AncsEvent((AncsEventKind)data[0], data[1], (AncsCategory)data[2], BitConverter.ToUInt32(data, 4));
        return true;
    }
}

public sealed record AncsNotification(
    uint Uid,
    AncsCategory Category,
    bool IsSilent,
    bool IsImportant,
    bool IsPreExisting,
    bool HasPositiveAction,
    bool HasNegativeAction,
    string AppId,
    string Title,
    string Subtitle,
    string Message,
    DateTime ReceivedAt);

public static class AncsAttribute
{
    public const byte AppIdentifier = 0, Title = 1, Subtitle = 2, Message = 3, MessageSize = 4, Date = 5;
    public const byte AppDisplayName = 0;
}

public static class AncsCommands
{
    /// <summary>Pede os detalhes de uma notificação. Atributos de texto levam o tamanho máximo.</summary>
    public static byte[] GetNotificationAttributes(uint uid, params (byte Id, int MaxLength)[] attributes)
    {
        var command = new List<byte> { 0 };
        command.AddRange(BitConverter.GetBytes(uid));
        foreach (var (id, maxLength) in attributes)
        {
            command.Add(id);
            if (maxLength > 0)
            {
                command.Add((byte)maxLength);
                command.Add((byte)(maxLength >> 8));
            }
        }
        return command.ToArray();
    }

    public static byte[] GetAppAttributes(string appId, params byte[] attributes)
    {
        var command = new List<byte> { 1 };
        command.AddRange(Encoding.UTF8.GetBytes(appId));
        command.Add(0);
        command.AddRange(attributes);
        return command.ToArray();
    }

    public static byte[] PerformAction(uint uid, bool positive)
    {
        var command = new List<byte> { 2 };
        command.AddRange(BitConverter.GetBytes(uid));
        command.Add(positive ? (byte)0 : (byte)1);
        return command.ToArray();
    }
}

/// <summary>
/// As respostas do iPhone chegam fatiadas em vários pacotes. Esta classe junta os pedaços
/// e devolve os atributos quando a resposta está completa.
/// </summary>
public sealed class AncsResponseAssembler
{
    private readonly List<byte> _buffer = new();
    private bool _active;
    private bool _isApp;
    private uint _uid;
    private int _count;

    public void ExpectNotification(uint uid, int attributeCount)
    {
        _buffer.Clear();
        _active = true;
        _isApp = false;
        _uid = uid;
        _count = attributeCount;
    }

    public void ExpectApp(int attributeCount)
    {
        _buffer.Clear();
        _active = true;
        _isApp = true;
        _count = attributeCount;
    }

    public void Reset()
    {
        _buffer.Clear();
        _active = false;
    }

    public bool TryFeed(byte[] chunk, out Dictionary<byte, string> attributes)
    {
        attributes = new Dictionary<byte, string>();
        if (!_active) return false;
        _buffer.AddRange(chunk);
        if (_buffer.Count == 0) return false;

        int i;
        if (_isApp)
        {
            if (_buffer[0] != 1) { _buffer.Clear(); return false; }
            int terminator = _buffer.IndexOf(0, 1);
            if (terminator < 0) return false;
            i = terminator + 1;
        }
        else
        {
            if (_buffer[0] != 0) { _buffer.Clear(); return false; }
            if (_buffer.Count < 5) return false;
            uint uid = (uint)(_buffer[1] | (_buffer[2] << 8) | (_buffer[3] << 16) | (_buffer[4] << 24));
            if (uid != _uid) { _buffer.Clear(); return false; }   // resto de uma resposta antiga
            i = 5;
        }

        for (int k = 0; k < _count; k++)
        {
            if (_buffer.Count < i + 3) return false;
            byte id = _buffer[i];
            int length = _buffer[i + 1] | (_buffer[i + 2] << 8);
            if (_buffer.Count < i + 3 + length) return false;
            attributes[id] = Encoding.UTF8.GetString(_buffer.GetRange(i + 3, length).ToArray());
            i += 3 + length;
        }

        Reset();
        return true;
    }
}

public enum MediaCommand : byte
{
    Play = 0, Pause = 1, TogglePlayPause = 2, NextTrack = 3, PreviousTrack = 4, VolumeUp = 5, VolumeDown = 6,
    AdvanceRepeatMode = 7, AdvanceShuffleMode = 8, SkipForward = 9, SkipBackward = 10,
    LikeTrack = 11, DislikeTrack = 12, BookmarkTrack = 13
}

public static class MediaCommands
{
    /// <summary>O iPhone avisa, a cada troca de app/faixa, quais comandos o app de música aceita agora.</summary>
    public static HashSet<MediaCommand> ParseSupported(byte[] data)
    {
        var set = new HashSet<MediaCommand>();
        foreach (var b in data)
            if (b <= (byte)MediaCommand.BookmarkTrack) set.Add((MediaCommand)b);
        return set;
    }

    /// <summary>O volume do iPhone tem 16 degraus; cada Volume+/- anda um.</summary>
    public const int VolumeSteps = 16;

    /// <summary>Quantos toques em Volume+ (positivo) ou Volume- (negativo) levam de um volume a outro.</summary>
    public static int VolumeStepsBetween(double from, double to) =>
        (int)Math.Round((Math.Clamp(to, 0, 1) - Math.Clamp(from, 0, 1)) * VolumeSteps);

    /// <summary>
    /// Pular para um ponto da música: o iPhone não aceita "ir para 1:23", só "avançar/voltar alguns segundos"
    /// (normalmente 15). Calcula quantos pulos chegam mais perto do ponto pedido.
    /// </summary>
    public static int SkipStepsBetween(double fromSeconds, double toSeconds, double skipInterval = 15, int max = 40)
    {
        if (skipInterval <= 0) return 0;
        int steps = (int)Math.Round((toSeconds - fromSeconds) / skipInterval);
        return Math.Clamp(steps, -max, max);
    }
}

public sealed record NowPlaying(
    string Player, string Title, string Artist, string Album,
    double Duration, double Elapsed, double Rate, bool IsPlaying, DateTime Timestamp, double Volume = -1)
{
    public bool HasVolume => Volume >= 0;

    public double ElapsedAt(DateTime now)
    {
        double elapsed = Elapsed;
        if (IsPlaying) elapsed += (now - Timestamp).TotalSeconds * (Rate <= 0 ? 1 : Rate);
        if (Duration > 0) elapsed = Math.Min(elapsed, Duration);
        return Math.Max(0, elapsed);
    }
}

/// <summary>Acumula as atualizações do AMS (uma por atributo) no estado completo do "Tocando Agora".</summary>
public sealed class NowPlayingTracker
{
    private string _player = "", _title = "", _artist = "", _album = "";
    private double _duration, _elapsed, _rate;
    private int _state;
    private double _volume = -1;
    private DateTime _stamp = DateTime.Now;

    /// <summary>Pedido de assinatura: player (nome, reprodução, volume).</summary>
    public static readonly byte[] SubscribePlayer = { 0, 0, 1, 2 };

    /// <summary>Pedido de assinatura: faixa (artista, álbum, título, duração).</summary>
    public static readonly byte[] SubscribeTrack = { 2, 0, 1, 2, 3 };

    public void Reset()
    {
        _player = _title = _artist = _album = "";
        _duration = _elapsed = _rate = 0;
        _volume = -1;
        _state = 0;
    }

    public NowPlaying? Apply(byte[] update, DateTime now)
    {
        if (update.Length < 3) return null;
        byte entity = update[0], attribute = update[1];
        var value = Encoding.UTF8.GetString(update, 3, update.Length - 3);

        if (entity == 0)
        {
            if (attribute == 0) _player = value;
            else if (attribute == 1)
            {
                var parts = value.Split(',');
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _state);
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _rate);
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _elapsed);
                    _stamp = now;
                }
            }
            else if (attribute == 2 && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
            {
                _volume = Math.Clamp(volume, 0, 1);
            }
        }
        else if (entity == 2)
        {
            switch (attribute)
            {
                case 0: _artist = value; break;
                case 1: _album = value; break;
                case 2: _title = value; break;
                case 3: double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _duration); break;
            }
        }
        else
        {
            return null;
        }

        return new NowPlaying(_player, _title, _artist, _album, _duration, _elapsed, _rate, _state == 1, _stamp, _volume);
    }
}

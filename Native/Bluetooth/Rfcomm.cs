using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Native.Core;
using Native.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Native.Bluetooth;

/// <summary>Canal de bytes sobre um socket RFCOMM (Bluetooth clássico).</summary>
public sealed class RfcommTransport : IObexTransport
{
    private readonly StreamSocket _socket;
    private readonly DataWriter _writer;
    private readonly DataReader _reader;
    private readonly BluetoothDevice _device;

    private RfcommTransport(BluetoothDevice device, StreamSocket socket)
    {
        _device = device;
        _socket = socket;
        _writer = new DataWriter(socket.OutputStream);
        _reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
    }

    /// <summary>Abre um serviço RFCOMM do iPhone pareado. Lança <see cref="ClassicException"/> com mensagem amigável.</summary>
    public static async Task<RfcommTransport> OpenAsync(Guid serviceUuid, string serviceLabel, ulong? preferredAddress, string nameFilter)
    {
        var paired = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        var candidates = paired
            .Where(d => d.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
            throw new ClassicException($"O iPhone não está pareado pelo Bluetooth clássico ({serviceLabel}).");

        Exception? last = null;
        foreach (var info in candidates)
        {
            BluetoothDevice? device = null;
            try
            {
                device = await BluetoothDevice.FromIdAsync(info.Id);
                if (device is null) continue;
                if (preferredAddress is ulong address && device.BluetoothAddress != address && candidates.Count > 1)
                {
                    // Com mais de um iPhone pareado, prefere o que está conectado no LE.
                    device.Dispose();
                    continue;
                }

                var services = await device.GetRfcommServicesForIdAsync(
                    RfcommServiceId.FromUuid(serviceUuid), BluetoothCacheMode.Uncached);
                if (services.Services.Count == 0)
                {
                    last = new ClassicException($"O iPhone não ofereceu {serviceLabel}. Confira as permissões no Bluetooth do iPhone.");
                    device.Dispose();
                    continue;
                }

                var service = services.Services[0];
                var socket = new StreamSocket();
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName,
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask().WaitAsync(TimeSpan.FromSeconds(12));
                return new RfcommTransport(device, socket);
            }
            catch (Exception ex)
            {
                last = ex;
                device?.Dispose();
            }
        }

        if (preferredAddress is not null && last is null)
            return await OpenAsync(serviceUuid, serviceLabel, null, nameFilter);

        Log.Warn("RFCOMM", $"{serviceLabel}: {Log.FirstLine(last?.Message)}");
        throw last as ClassicException
              ?? new ClassicException(last is TimeoutException
                  ? $"O iPhone não respondeu ({serviceLabel}). Ele está por perto e desbloqueado?"
                  : $"Não consegui abrir {serviceLabel} no iPhone.");
    }

    public async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        _writer.WriteBytes(data);
        await _writer.StoreAsync().AsTask(ct);
    }

    public async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        while (_reader.UnconsumedBufferLength < count)
        {
            uint got = await _reader.LoadAsync((uint)(count - _reader.UnconsumedBufferLength)).AsTask(ct);
            if (got == 0) throw new ClassicException("O iPhone fechou a conexão.");
        }
        var bytes = new byte[count];
        _reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>Lê o que chegar (até <paramref name="max"/> bytes). Usado pelos comandos AT de texto.</summary>
    public async Task<byte[]> ReadSomeAsync(int max, CancellationToken ct)
    {
        if (_reader.UnconsumedBufferLength == 0)
        {
            uint got = await _reader.LoadAsync((uint)max).AsTask(ct);
            if (got == 0) throw new ClassicException("O iPhone fechou a conexão.");
        }
        var bytes = new byte[Math.Min(max, (int)_reader.UnconsumedBufferLength)];
        _reader.ReadBytes(bytes);
        return bytes;
    }

    public void Dispose()
    {
        try { _writer.DetachStream(); } catch { }
        try { _reader.DetachStream(); } catch { }
        try { _writer.Dispose(); } catch { }
        try { _reader.Dispose(); } catch { }
        try { _socket.Dispose(); } catch { }
        try { _device.Dispose(); } catch { }
    }
}

/// <summary>Falha esperada do Bluetooth clássico, com texto pronto para mostrar ao usuário.</summary>
public sealed class ClassicException : Exception
{
    public ClassicException(string message) : base(message) { }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Native.Core;
using Native.Protocol;
using Native.Services;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace Native.Bluetooth;

public enum LinkState
{
    Searching, Connecting, Connected,
    NoBluetooth, BluetoothOff, NotPaired, OutOfRange, AccessDenied, Busy, Error
}

internal sealed class LinkException : Exception
{
    public LinkState State { get; }

    public LinkException(LinkState state, string message) : base(message)
    {
        State = state;
    }
}

/// <summary>
/// Ligação Bluetooth LE com o iPhone: notificações (ANCS), mídia (AMS), bateria e modelo.
/// Mantém a conexão viva, percebe quando ela morre e reconecta sozinho.
/// </summary>
public sealed class AncsClient
{
    private static readonly Guid AncsService      = Guid.Parse("7905F431-B5CE-4E99-A40F-4B1E122D00D0");
    private static readonly Guid NotifSourceUuid  = Guid.Parse("9FBF120D-6301-42D9-8C58-25E699A21DBD");
    private static readonly Guid ControlPointUuid = Guid.Parse("69D1D8F3-45E1-49A8-9821-9BBDFDAAD9D9");
    private static readonly Guid DataSourceUuid   = Guid.Parse("22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB");

    private static readonly Guid AmsService       = Guid.Parse("89D3502B-0F36-433A-8EF4-C502AD55F8DC");
    private static readonly Guid AmsRemoteCommand = Guid.Parse("9B3C81D8-57B1-4A8A-B8DF-0E56F7CA51C2");
    private static readonly Guid AmsEntityUpdate  = Guid.Parse("2F7CABCE-808D-411F-9A0C-BB92BA96C102");

    private BluetoothLEDevice? _device;
    private GattSession? _session;
    private GattDeviceService? _ancsService, _batteryService, _amsService;
    private GattCharacteristic? _notifSource, _controlPoint, _dataSource, _battery, _remoteCommand, _entityUpdate;

    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly object _rxGate = new();
    private readonly AncsResponseAssembler _assembler = new();
    private TaskCompletionSource<Dictionary<byte, string>>? _pending;

    private readonly object _mediaGate = new();
    private readonly NowPlayingTracker _media = new();

    private readonly List<Radio> _radios = new();
    private TaskCompletionSource _wake = NewSignal();
    private string _lastMessage = "";

    public event Action<LinkState, string>? StateChanged;
    public event Action<AncsNotification>? NotificationReceived;
    public event Action<uint>? NotificationRemoved;
    public event Action<int>? BatteryChanged;
    public event Action<NowPlaying?>? NowPlayingChanged;

    /// <summary>Comandos que o app de música do iPhone aceita agora (muda conforme o app e a faixa).</summary>
    public event Action<IReadOnlyCollection<MediaCommand>>? MediaCommandsChanged;

    public LinkState State { get; private set; } = LinkState.Searching;
    public string DeviceName { get; private set; } = "iPhone";
    public string NameFilter { get; set; } = "iPhone";

    /// <summary>Endereço Bluetooth do iPhone conectado (o mesmo usado no Bluetooth clássico).</summary>
    public ulong? DeviceAddress { get; private set; }

    /// <summary>Identificador do modelo informado pelo iPhone (ex.: "iPhone16,1").</summary>
    public string? ModelIdentifier { get; private set; }

    // ---------------------------------------------------------------- ciclo de vida

    public async Task RunAsync(CancellationToken ct)
    {
        await WatchRadiosAsync();
        int failures = 0;

        while (!ct.IsCancellationRequested)
        {
            bool wasConnected = false;
            try
            {
                wasConnected = await ConnectAndListenAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (LinkException ex)
            {
                Report(ex.State, ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error("ANCS", "Falha na conexão", ex);
                Report(LinkState.Error, "Não foi possível conectar. Tentando de novo…");
            }
            finally
            {
                Teardown();
            }

            failures = wasConnected ? 0 : failures + 1;
            var delay = TimeSpan.FromSeconds(wasConnected ? 2 : Math.Min(3 + failures * 2, 15));
            try { await Task.WhenAny(Task.Delay(delay, ct), TakeSignal()); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Derruba a sessão atual (se houver) e conecta de novo já. Usado ao voltar da suspensão.</summary>
    public void RequestReconnect(string reason)
    {
        Log.Info("ANCS", $"Reconexão pedida: {reason}");
        _wake.TrySetResult();
    }

    public static async Task<bool> TurnOnBluetoothAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return false;
            bool any = false;
            foreach (var radio in await Radio.GetRadiosAsync())
            {
                if (radio.Kind != RadioKind.Bluetooth) continue;
                any |= await radio.SetStateAsync(RadioState.On) == RadioAccessStatus.Allowed;
            }
            return any;
        }
        catch (Exception ex)
        {
            Log.Warn("ANCS", $"Não deu para ligar o Bluetooth: {Log.FirstLine(ex.Message)}");
            return false;
        }
    }

    // ---------------------------------------------------------------- comandos

    public async Task PerformActionAsync(uint uid, bool positive)
    {
        await _commandLock.WaitAsync();
        try
        {
            var cp = _controlPoint;
            if (cp is null) return;
            await cp.WriteValueWithResultAsync(AncsCommands.PerformAction(uid, positive).AsBuffer(), GattWriteOption.WriteWithResponse);
        }
        catch
        {
            // A notificação pode já ter sumido do iPhone.
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task SendMediaCommandAsync(MediaCommand command)
    {
        var rc = _remoteCommand;
        if (rc is null) return;
        try
        {
            await rc.WriteValueWithResultAsync(new[] { (byte)command }.AsBuffer(), GattWriteOption.WriteWithResponse);
        }
        catch
        {
            // O app de música pode não aceitar o comando.
        }
    }

    /// <summary>Pergunta ao iPhone o nome do app como aparece na tela de início.</summary>
    public async Task<string?> GetAppDisplayNameAsync(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;
        var attrs = await RequestAsync(
            AncsCommands.GetAppAttributes(appId, AncsAttribute.AppDisplayName),
            a => a.ExpectApp(1), 3000);
        return attrs is not null && attrs.TryGetValue(AncsAttribute.AppDisplayName, out var name) && name.Trim().Length > 0
            ? name.Trim()
            : null;
    }

    // ---------------------------------------------------------------- conexão

    private async Task<bool> ConnectAndListenAsync(CancellationToken ct)
    {
        var radio = await CheckRadioAsync();
        if (radio == LinkState.NoBluetooth) throw new LinkException(LinkState.NoBluetooth, "Este PC não tem Bluetooth");
        if (radio == LinkState.BluetoothOff) throw new LinkException(LinkState.BluetoothOff, "Bluetooth desligado");

        // Estados de "falta alguma coisa" ficam na tela até mudarem; não piscam para "Procurando".
        if (State is not (LinkState.NotPaired or LinkState.OutOfRange or LinkState.AccessDenied or LinkState.Busy))
            Report(LinkState.Searching, "Procurando iPhone…");

        PhoneLinkGuard.Stop();

        var paired = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
        var candidates = paired.Where(d => d.Name.Contains(NameFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0) throw new LinkException(LinkState.NotPaired, "Nenhum iPhone pareado");

        LinkException? failure = null;
        foreach (var info in candidates)
        {
            ct.ThrowIfCancellationRequested();

            BluetoothLEDevice? device;
            try { device = await BluetoothLEDevice.FromIdAsync(info.Id); }
            catch { continue; }                       // registro antigo ou inválido no Windows
            if (device is null) continue;

            bool ready = false;
            try { ready = await TrySetupAsync(device); }
            catch (LinkException ex) { failure = ex; }

            if (ready) break;
            Teardown();
            device.Dispose();
        }

        var connected = _device;
        if (connected is null) throw failure ?? new LinkException(LinkState.OutOfRange, "iPhone fora de alcance");

        DeviceAddress = connected.BluetoothAddress;
        Report(LinkState.Connected, "Conectado");

        var disconnected = NewSignal();
        connected.ConnectionStatusChanged += (sender, _) =>
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected) disconnected.TrySetResult();
        };
        var wake = TakeSignal();

        using var cancel = ct.Register(() => disconnected.TrySetCanceled());
        while (true)
        {
            var tick = Task.Delay(TimeSpan.FromSeconds(45));
            var done = await Task.WhenAny(disconnected.Task, wake, tick);

            if (done == disconnected.Task)
            {
                await disconnected.Task;              // propaga o cancelamento ao sair do app
                Log.Info("ANCS", "O iPhone desconectou");
                break;
            }
            if (done == wake) break;
            if (!await IsAliveAsync())
            {
                Log.Warn("ANCS", "O iPhone parou de responder; refazendo a conexão");
                break;
            }
        }

        Report(LinkState.Searching, "Reconectando…");
        return true;
    }

    private async Task<bool> TrySetupAsync(BluetoothLEDevice device)
    {
        if (State is LinkState.Searching or LinkState.Error) Report(LinkState.Connecting, "Conectando…");

        _session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
        _session.MaintainConnection = true;

        var services = await device.GetGattServicesForUuidAsync(AncsService, BluetoothCacheMode.Uncached);
        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) return false;

        _ancsService = services.Services[0];
        await _ancsService.RequestAccessAsync();

        var open = await _ancsService.OpenAsync(GattSharingMode.SharedReadAndWrite);
        if (open == GattOpenStatus.SharingViolation)
            throw new LinkException(LinkState.Busy, "Outro programa está usando o iPhone");

        var chars = await _ancsService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
        if (chars.Status == GattCommunicationStatus.AccessDenied)
            throw new LinkException(LinkState.AccessDenied, "O iPhone não liberou as notificações");
        if (chars.Status != GattCommunicationStatus.Success) return false;

        _notifSource  = chars.Characteristics.FirstOrDefault(c => c.Uuid == NotifSourceUuid);
        _controlPoint = chars.Characteristics.FirstOrDefault(c => c.Uuid == ControlPointUuid);
        _dataSource   = chars.Characteristics.FirstOrDefault(c => c.Uuid == DataSourceUuid);
        if (_notifSource is null || _controlPoint is null || _dataSource is null) return false;

        _dataSource.ValueChanged += OnDataSource;
        _notifSource.ValueChanged += OnNotificationSource;

        // O Data Source precisa ser assinado antes do Notification Source.
        var a = await _dataSource.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);
        var b = await _notifSource.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);
        if (a != GattCommunicationStatus.Success || b != GattCommunicationStatus.Success) return false;

        _device = device;
        DeviceName = string.IsNullOrWhiteSpace(device.Name) ? "iPhone" : device.Name;

        await TryReadModelAsync(device);
        await TrySetupBatteryAsync(device);
        await TrySetupMediaAsync(device);
        return true;
    }

    private async Task TryReadModelAsync(BluetoothLEDevice device)
    {
        try
        {
            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.DeviceInformation, BluetoothCacheMode.Uncached);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) return;
            using var service = services.Services[0];
            var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.ModelNumberString, BluetoothCacheMode.Uncached);
            if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0) return;
            var read = await chars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
            if (read.Status == GattCommunicationStatus.Success)
                ModelIdentifier = Encoding.UTF8.GetString(read.Value.ToArray()).Trim('\0', ' ');
        }
        catch
        {
            // O modelo é só informativo.
        }
    }

    private async Task TrySetupBatteryAsync(BluetoothLEDevice device)
    {
        try
        {
            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) return;
            _batteryService = services.Services[0];

            var chars = await _batteryService.GetCharacteristicsForUuidAsync(
                GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
            if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0) return;
            _battery = chars.Characteristics[0];

            var read = await _battery.ReadValueAsync(BluetoothCacheMode.Uncached);
            if (read.Status == GattCommunicationStatus.Success) PublishBattery(read.Value.ToArray());

            _battery.ValueChanged += OnBattery;
            await _battery.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
        }
        catch
        {
            // A bateria é opcional.
        }
    }

    private async Task TrySetupMediaAsync(BluetoothLEDevice device)
    {
        try
        {
            var services = await device.GetGattServicesForUuidAsync(AmsService, BluetoothCacheMode.Uncached);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) return;
            _amsService = services.Services[0];
            await _amsService.OpenAsync(GattSharingMode.SharedReadAndWrite);

            var chars = await _amsService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            if (chars.Status != GattCommunicationStatus.Success)
            {
                Log.Info("AMS", $"Serviço de mídia indisponível ({chars.Status})");
                return;
            }
            _remoteCommand = chars.Characteristics.FirstOrDefault(c => c.Uuid == AmsRemoteCommand);
            _entityUpdate = chars.Characteristics.FirstOrDefault(c => c.Uuid == AmsEntityUpdate);

            if (_remoteCommand is not null)
            {
                _remoteCommand.ValueChanged += OnRemoteCommands;
                await _remoteCommand.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
            }

            if (_entityUpdate is not null)
            {
                _entityUpdate.ValueChanged += OnEntityUpdate;
                await _entityUpdate.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
                await _entityUpdate.WriteValueWithResultAsync(NowPlayingTracker.SubscribePlayer.AsBuffer(), GattWriteOption.WriteWithResponse);
                await _entityUpdate.WriteValueWithResultAsync(NowPlayingTracker.SubscribeTrack.AsBuffer(), GattWriteOption.WriteWithResponse);
                Log.Info("AMS", "Tocando Agora ativo");
            }
        }
        catch (Exception ex)
        {
            Log.Info("AMS", $"Serviço de mídia não iniciou: {Log.FirstLine(ex.Message)}");
        }
    }

    private async Task<bool> IsAliveAsync()
    {
        var device = _device;
        if (device is null || device.ConnectionStatus == BluetoothConnectionStatus.Disconnected) return false;

        var battery = _battery;
        if (battery is null) return true;
        try
        {
            var read = await battery.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask().WaitAsync(TimeSpan.FromSeconds(8));
            if (read.Status == GattCommunicationStatus.Success) PublishBattery(read.Value.ToArray());
            return read.Status != GattCommunicationStatus.Unreachable;
        }
        catch
        {
            return false;
        }
    }

    private void Teardown()
    {
        if (_dataSource is not null) _dataSource.ValueChanged -= OnDataSource;
        if (_notifSource is not null) _notifSource.ValueChanged -= OnNotificationSource;
        if (_battery is not null) _battery.ValueChanged -= OnBattery;
        if (_entityUpdate is not null) _entityUpdate.ValueChanged -= OnEntityUpdate;
        if (_remoteCommand is not null) _remoteCommand.ValueChanged -= OnRemoteCommands;
        bool hadMedia = _entityUpdate is not null;
        _dataSource = _notifSource = _controlPoint = _battery = _remoteCommand = _entityUpdate = null;

        try { _amsService?.Dispose(); } catch { }
        try { _ancsService?.Dispose(); } catch { }
        try { _batteryService?.Dispose(); } catch { }
        try { _session?.Dispose(); } catch { }
        try { _device?.Dispose(); } catch { }
        _amsService = _ancsService = _batteryService = null;
        _session = null;
        _device = null;

        lock (_mediaGate) _media.Reset();
        if (hadMedia) NowPlayingChanged?.Invoke(null);

        lock (_rxGate)
        {
            _pending?.TrySetResult(new Dictionary<byte, string>());
            _pending = null;
            _assembler.Reset();
        }
    }

    // ---------------------------------------------------------------- eventos do iPhone

    private void OnNotificationSource(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (!AncsEvent.TryParse(args.CharacteristicValue.ToArray(), out var e)) return;

        if (e.Kind == AncsEventKind.Removed)
        {
            NotificationRemoved?.Invoke(e.Uid);
            return;
        }

        _ = FetchAndPublishAsync(e);
    }

    private async Task FetchAndPublishAsync(AncsEvent e)
    {
        var attrs = await GetNotificationAttributesAsync(e.Uid);
        if (attrs is null || attrs.Count == 0)
        {
            await Task.Delay(300);
            attrs = await GetNotificationAttributesAsync(e.Uid);
        }
        if (attrs is null || attrs.Count == 0)
        {
            Log.Warn("ANCS", "Não consegui ler os detalhes de uma notificação");
            return;
        }

        string Get(byte id) => attrs.TryGetValue(id, out var v) ? v.Trim() : "";

        NotificationReceived?.Invoke(new AncsNotification(
            e.Uid, e.Category, e.IsSilent, e.IsImportant, e.IsPreExisting, e.HasPositiveAction, e.HasNegativeAction,
            AppId: Get(AncsAttribute.AppIdentifier),
            Title: Get(AncsAttribute.Title),
            Subtitle: Get(AncsAttribute.Subtitle),
            Message: Get(AncsAttribute.Message),
            ReceivedAt: TimeFormat.ParseCompact(Get(AncsAttribute.Date)) ?? DateTime.Now));
    }

    private Task<Dictionary<byte, string>?> GetNotificationAttributesAsync(uint uid) =>
        RequestAsync(
            AncsCommands.GetNotificationAttributes(uid,
                (AncsAttribute.AppIdentifier, 0), (AncsAttribute.Title, 96), (AncsAttribute.Subtitle, 96),
                (AncsAttribute.Message, 600), (AncsAttribute.Date, 0)),
            a => a.ExpectNotification(uid, 5), 4000);

    private async Task<Dictionary<byte, string>?> RequestAsync(byte[] command, Action<AncsResponseAssembler> expect, int timeoutMs)
    {
        await _commandLock.WaitAsync();
        try
        {
            var cp = _controlPoint;
            if (cp is null) return null;

            var tcs = new TaskCompletionSource<Dictionary<byte, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_rxGate)
            {
                expect(_assembler);
                _pending = tcs;
            }

            var result = await cp.WriteValueWithResultAsync(command.AsBuffer(), GattWriteOption.WriteWithResponse);
            if (result.Status != GattCommunicationStatus.Success) return null;

            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            return done == tcs.Task ? tcs.Task.Result : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            lock (_rxGate)
            {
                _pending = null;
                _assembler.Reset();
            }
            _commandLock.Release();
        }
    }

    private void OnDataSource(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var chunk = args.CharacteristicValue.ToArray();
        TaskCompletionSource<Dictionary<byte, string>> tcs;
        Dictionary<byte, string> attributes;

        lock (_rxGate)
        {
            if (_pending is null) return;
            if (!_assembler.TryFeed(chunk, out attributes)) return;   // resposta ainda incompleta
            tcs = _pending;
            _pending = null;
        }
        tcs.TrySetResult(attributes);
    }

    private void OnBattery(GattCharacteristic sender, GattValueChangedEventArgs args) =>
        PublishBattery(args.CharacteristicValue.ToArray());

    private void PublishBattery(byte[] value)
    {
        if (value.Length > 0 && value[0] <= 100) BatteryChanged?.Invoke(value[0]);
    }

    private void OnRemoteCommands(GattCharacteristic sender, GattValueChangedEventArgs args) =>
        MediaCommandsChanged?.Invoke(MediaCommands.ParseSupported(args.CharacteristicValue.ToArray()));

    private void OnEntityUpdate(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        NowPlaying? snapshot;
        lock (_mediaGate) snapshot = _media.Apply(args.CharacteristicValue.ToArray(), DateTime.Now);
        if (snapshot is not null) NowPlayingChanged?.Invoke(snapshot);
    }

    // ---------------------------------------------------------------- rádio e utilidades

    private static async Task<LinkState?> CheckRadioAsync()
    {
        try
        {
            var radios = (await Radio.GetRadiosAsync()).Where(r => r.Kind == RadioKind.Bluetooth).ToList();
            if (radios.Count == 0) return LinkState.NoBluetooth;
            if (radios.All(r => r.State != RadioState.On)) return LinkState.BluetoothOff;
        }
        catch
        {
            // Sem acesso à lista de rádios: seguimos e deixamos a conexão dizer o que há.
        }
        return null;
    }

    private async Task WatchRadiosAsync()
    {
        try
        {
            foreach (var radio in await Radio.GetRadiosAsync())
            {
                if (radio.Kind != RadioKind.Bluetooth) continue;
                radio.StateChanged += (sender, _) =>
                    RequestReconnect($"Bluetooth {(sender.State == RadioState.On ? "ligado" : "desligado")}");
                _radios.Add(radio);
            }
        }
        catch
        {
            // Sem aviso de mudança do rádio; a reconexão periódica cobre.
        }
    }

    private void Report(LinkState state, string message)
    {
        if (state == State && message == _lastMessage) return;
        State = state;
        _lastMessage = message;
        Log.Info("ANCS", $"Estado: {state} — {message}");
        StateChanged?.Invoke(state, message);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Devolve o sinal de "acorde agora"; se o anterior já disparou, arma um novo.</summary>
    private Task TakeSignal()
    {
        var current = _wake;
        if (current.Task.IsCompleted)
        {
            current = NewSignal();
            _wake = current;
        }
        return current.Task;
    }
}

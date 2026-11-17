using System;

namespace Native.Protocol;

public enum ChargeState { Unknown, Charging, Discharging, Full }

/// <summary>
/// O iPhone não conta por Bluetooth se está na tomada: o serviço de bateria só manda a porcentagem.
/// Então o Native deduz: a porcentagem subiu, está carregando; caiu, não está.
/// </summary>
public sealed class ChargeTracker
{
    /// <summary>Sem subir por esse tempo, deixa de afirmar que está carregando (o cabo pode ter saído com a bateria parada).</summary>
    public static readonly TimeSpan ChargingTimeout = TimeSpan.FromMinutes(25);

    private int? _last;
    private DateTime _lastRise;

    public ChargeState State { get; private set; } = ChargeState.Unknown;

    public ChargeState Update(int level, DateTime now)
    {
        if (_last is int previous)
        {
            if (level > previous)
            {
                State = level >= 100 ? ChargeState.Full : ChargeState.Charging;
                _lastRise = now;
            }
            else if (level < previous)
            {
                State = ChargeState.Discharging;
            }
            else if (level >= 100 && State == ChargeState.Charging)
            {
                State = ChargeState.Full;
            }
            else if (State == ChargeState.Charging && now - _lastRise > ChargingTimeout)
            {
                State = ChargeState.Unknown;
            }
        }
        _last = level;
        return State;
    }

    /// <summary>Chamado de tempos em tempos mesmo sem leitura nova, para o "carregando" não ficar preso.</summary>
    public ChargeState Tick(DateTime now)
    {
        if (State == ChargeState.Charging && now - _lastRise > ChargingTimeout) State = ChargeState.Unknown;
        return State;
    }

    public void Reset()
    {
        _last = null;
        State = ChargeState.Unknown;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Native.Protocol;

namespace Native.Tests;

/// <summary>Mini estrutura de testes: sem dependências, para rodar em qualquer lugar.</summary>
public static class Check
{
    public static void Equal<T>(T expected, T actual, string? what = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what ?? "valor"}: esperado <{expected}>, veio <{actual}>");
    }

    public static void True(bool condition, string what)
    {
        if (!condition) throw new Exception($"deveria ser verdadeiro: {what}");
    }

    public static void Null(object? value, string what)
    {
        if (value is not null) throw new Exception($"deveria ser nulo: {what} (veio <{value}>)");
    }

    public static void Bytes(byte[] expected, byte[] actual, string? what = null)
    {
        if (!expected.SequenceEqual(actual))
            throw new Exception($"{what ?? "bytes"}: esperado {Convert.ToHexString(expected)}, veio {Convert.ToHexString(actual)}");
    }

    public static async Task ThrowsAsync<T>(Func<Task> action, string what) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception($"deveria lançar {typeof(T).Name}: {what}");
    }
}

/// <summary>
/// Um "cabo" em memória ligando dois lados OBEX. Opcionalmente fatia os envios em pedaços
/// pequenos, como acontece num canal Bluetooth de verdade.
/// </summary>
public sealed class PipeEnd : IObexTransport
{
    private readonly ChannelReader<byte[]> _incoming;
    private readonly ChannelWriter<byte[]> _outgoing;
    private readonly int _fragment;
    private readonly List<byte> _pending = new();

    private PipeEnd(ChannelReader<byte[]> incoming, ChannelWriter<byte[]> outgoing, int fragment)
    {
        _incoming = incoming;
        _outgoing = outgoing;
        _fragment = fragment;
    }

    public static (PipeEnd A, PipeEnd B) CreatePair(int fragment = 0)
    {
        var ab = Channel.CreateUnbounded<byte[]>();
        var ba = Channel.CreateUnbounded<byte[]>();
        return (new PipeEnd(ba.Reader, ab.Writer, fragment), new PipeEnd(ab.Reader, ba.Writer, fragment));
    }

    public async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        if (_fragment <= 0)
        {
            await _outgoing.WriteAsync(data, ct);
            return;
        }
        for (int i = 0; i < data.Length; i += _fragment)
            await _outgoing.WriteAsync(data.AsSpan(i, Math.Min(_fragment, data.Length - i)).ToArray(), ct);
    }

    public async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        while (_pending.Count < count)
        {
            if (!await _incoming.WaitToReadAsync(ct)) throw new InvalidOperationException("conexão fechada");
            while (_incoming.TryRead(out var chunk)) _pending.AddRange(chunk);
        }
        var result = _pending.GetRange(0, count).ToArray();
        _pending.RemoveRange(0, count);
        return result;
    }

    public void Dispose() => _outgoing.TryComplete();
}

public sealed class TestCase
{
    public string Name { get; }
    public Func<Task> Run { get; }

    public TestCase(string name, Func<Task> run)
    {
        Name = name;
        Run = run;
    }

    public TestCase(string name, Action run) : this(name, () => { run(); return Task.CompletedTask; }) { }
}

public static class Program
{
    public static async Task<int> Main()
    {
        var tests = new List<TestCase>();
        tests.AddRange(TextTests.All());
        tests.AddRange(AncsTests.All());
        tests.AddRange(AncsTests.Media());
        tests.AddRange(ShareTests.All());
        tests.AddRange(ObexTests.All());
        tests.AddRange(MapTests.All());
        tests.AddRange(ContactTests.All());

        int failed = 0;
        foreach (var test in tests)
        {
            try
            {
                var run = test.Run();
                if (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20))) != run)
                    throw new TimeoutException("o teste travou");
                await run;
                Console.WriteLine($"  ok    {test.Name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FALHA {test.Name}\n        {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"{tests.Count} testes, todos passaram."
            : $"{tests.Count} testes, {failed} falharam.");
        return failed == 0 ? 0 : 1;
    }
}

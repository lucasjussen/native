using System;
using System.Collections.Generic;
using System.Text;

namespace Native.Protocol.Share;

/// <summary>
/// Gerador de QR Code (modo byte, correção M, versões 1 a 10), sem dependências.
/// Segue a ISO/IEC 18004; a estrutura é a mesma do gerador de referência de Project Nayuki.
/// </summary>
public sealed class QrCode
{
    public int Version { get; }
    public int Size { get; }
    public int Mask { get; private set; }
    private readonly bool[,] _modules;     // [linha, coluna]
    private readonly bool[,] _isFunction;

    public bool this[int row, int col] => _modules[row, col];

    // Correção M: (EC por bloco, blocos do grupo 1, dados no grupo 1, blocos do grupo 2, dados no grupo 2)
    private static readonly (int Ec, int B1, int D1, int B2, int D2)[] TableM =
    {
        default,
        (10, 1, 16, 0, 0), (16, 1, 28, 0, 0), (26, 1, 44, 0, 0), (18, 2, 32, 0, 0), (24, 2, 43, 0, 0),
        (16, 4, 27, 0, 0), (18, 4, 31, 0, 0), (22, 2, 38, 2, 39), (22, 3, 36, 2, 37), (26, 4, 43, 1, 44),
    };

    private static readonly int[][] Alignment =
    {
        Array.Empty<int>(), Array.Empty<int>(),
        new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
        new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
    };

    public const int MaxVersion = 10;

    public static int DataCapacity(int version)
    {
        var t = TableM[version];
        return t.B1 * t.D1 + t.B2 * t.D2;
    }

    /// <summary>Codifica o texto (UTF-8) no menor QR que couber.</summary>
    public static QrCode Encode(string text) => Encode(Encoding.UTF8.GetBytes(text));

    public static QrCode Encode(byte[] data)
    {
        for (int v = 1; v <= MaxVersion; v++)
        {
            int countBits = v <= 9 ? 8 : 16;
            int needBits = 4 + countBits + data.Length * 8;
            if (needBits <= DataCapacity(v) * 8) return new QrCode(v, data);
        }
        throw new ArgumentException($"Texto grande demais para o QR Code ({data.Length} bytes).");
    }

    private QrCode(int version, byte[] data)
    {
        Version = version;
        Size = version * 4 + 17;
        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];

        DrawFunctionPatterns();
        var codewords = AddErrorCorrection(BuildDataCodewords(data));
        DrawCodewords(codewords);

        // Escolhe a máscara com menor penalidade.
        int best = 0, bestPenalty = int.MaxValue;
        for (int m = 0; m < 8; m++)
        {
            ApplyMask(m);
            DrawFormatBits(m);
            int p = Penalty();
            if (p < bestPenalty) { bestPenalty = p; best = m; }
            ApplyMask(m);   // desfaz (XOR)
        }
        Mask = best;
        ApplyMask(best);
        DrawFormatBits(best);
    }

    // ------------------------------------------------------------ dados

    private byte[] BuildDataCodewords(byte[] data)
    {
        int capacity = DataCapacity(Version);
        var bits = new List<bool>(capacity * 8);
        void Append(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        Append(0b0100, 4);                                    // modo byte
        Append(data.Length, Version <= 9 ? 8 : 16);
        foreach (var b in data) Append(b, 8);
        Append(0, Math.Min(4, capacity * 8 - bits.Count));    // terminador
        Append(0, (8 - bits.Count % 8) % 8);
        for (int pad = 0xEC; bits.Count < capacity * 8; pad ^= 0xEC ^ 0x11) Append(pad, 8);

        var result = new byte[capacity];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i]) result[i >> 3] |= (byte)(0x80 >> (i & 7));
        return result;
    }

    private byte[] AddErrorCorrection(byte[] data)
    {
        var t = TableM[Version];
        var divisor = ReedSolomonDivisor(t.Ec);
        var blocks = new List<byte[]>();
        var ecs = new List<byte[]>();
        int k = 0;
        for (int i = 0; i < t.B1 + t.B2; i++)
        {
            int len = i < t.B1 ? t.D1 : t.D2;
            var block = new byte[len];
            Array.Copy(data, k, block, 0, len);
            k += len;
            blocks.Add(block);
            ecs.Add(ReedSolomonRemainder(block, divisor));
        }

        var result = new List<byte>();
        int maxLen = Math.Max(t.D1, t.D2);
        for (int i = 0; i < maxLen; i++)
            foreach (var b in blocks)
                if (i < b.Length) result.Add(b[i]);
        for (int i = 0; i < t.Ec; i++)
            foreach (var e in ecs) result.Add(e[i]);
        return result.ToArray();
    }

    private static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < result.Length; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < result.Length) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < result.Length; i++) result[i] ^= Multiply(divisor[i], factor);
        }
        return result;
    }

    private static byte Multiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    // ------------------------------------------------------------ desenho

    private void SetFunction(int x, int y, bool dark)
    {
        _modules[y, x] = dark;
        _isFunction[y, x] = true;
    }

    private void DrawFunctionPatterns()
    {
        for (int i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }
        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var pos = Alignment[Version];
        for (int i = 0; i < pos.Length; i++)
            for (int j = 0; j < pos.Length; j++)
            {
                bool corner = (i == 0 && j == 0) || (i == 0 && j == pos.Length - 1) || (i == pos.Length - 1 && j == 0);
                if (!corner) DrawAlignment(pos[i], pos[j]);
            }

        DrawFormatBits(0);   // reserva a área
        DrawVersion();
    }

    private void DrawFinder(int x, int y)
    {
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) SetFunction(xx, yy, dist != 2 && dist != 4);
            }
    }

    private void DrawAlignment(int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
    }

    private void DrawFormatBits(int mask)
    {
        int data = (0 << 3) | mask;   // nível M = 00
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;

        for (int i = 0; i <= 5; i++) SetFunction(8, i, Bit(i));
        SetFunction(8, 7, Bit(6));
        SetFunction(8, 8, Bit(7));
        SetFunction(7, 8, Bit(8));
        for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(i));

        for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(i));
        for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(i));
        SetFunction(8, Size - 8, true);   // módulo escuro fixo
    }

    private void DrawVersion()
    {
        if (Version < 7) return;
        int rem = Version;
        for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        int bits = (Version << 12) | rem;
        for (int i = 0; i < 18; i++)
        {
            bool bit = ((bits >> i) & 1) != 0;
            int a = Size - 11 + i % 3, b = i / 3;
            SetFunction(a, b, bit);
            SetFunction(b, a, bit);
        }
    }

    private void DrawCodewords(byte[] data)
    {
        int i = 0;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < Size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? Size - 1 - vert : vert;
                    if (!_isFunction[y, x] && i < data.Length * 8)
                    {
                        _modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
        }
    }

    private void ApplyMask(int mask)
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (_isFunction[y, x]) continue;
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert) _modules[y, x] = !_modules[y, x];
            }
    }

    private int Penalty()
    {
        int penalty = 0;
        // Regra 1: sequências de 5+ da mesma cor em linhas e colunas.
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < Size; a++)
            {
                int run = 1;
                for (int b = 1; b < Size; b++)
                {
                    bool cur = pass == 0 ? _modules[a, b] : _modules[b, a];
                    bool prev = pass == 0 ? _modules[a, b - 1] : _modules[b - 1, a];
                    if (cur == prev) { run++; if (run == 5) penalty += 3; else if (run > 5) penalty++; }
                    else run = 1;
                }
            }
        // Regra 2: blocos 2x2.
        for (int y = 0; y < Size - 1; y++)
            for (int x = 0; x < Size - 1; x++)
            {
                bool c = _modules[y, x];
                if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1]) penalty += 3;
            }
        // Regra 3: padrões parecidos com o localizador (1:1:3:1:1 com 4 claros de um lado).
        bool[] p1 = { true, false, true, true, true, false, true, false, false, false, false };
        bool[] p2 = { false, false, false, false, true, false, true, true, true, false, true };
        for (int y = 0; y < Size; y++)
            for (int x = 0; x + 11 <= Size; x++)
                for (int pass = 0; pass < 2; pass++)
                {
                    bool m1 = true, m2 = true;
                    for (int k = 0; k < 11 && (m1 || m2); k++)
                    {
                        bool v = pass == 0 ? _modules[y, x + k] : _modules[x + k, y];
                        if (v != p1[k]) m1 = false;
                        if (v != p2[k]) m2 = false;
                    }
                    if (m1) penalty += 40;
                    if (m2) penalty += 40;
                }
        // Regra 4: equilíbrio entre escuro e claro.
        int dark = 0;
        foreach (var m in _modules) if (m) dark++;
        int total = Size * Size;
        int k4 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        penalty += Math.Max(0, k4) * 10;
        return penalty;
    }

    /// <summary>Desenho em texto (para testes): '#' escuro, ' ' claro.</summary>
    public string ToAscii(int border = 0)
    {
        var sb = new StringBuilder();
        for (int y = -border; y < Size + border; y++)
        {
            for (int x = -border; x < Size + border; x++)
                sb.Append(x >= 0 && y >= 0 && x < Size && y < Size && _modules[y, x] ? '#' : ' ');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

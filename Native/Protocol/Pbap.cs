using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Native.Protocol;

// Phone Book Access Profile (PBAP): contatos e histórico de ligações do iPhone.

public static class PbapConstants
{
    /// <summary>Serviço de agenda no iPhone (PSE).</summary>
    public static readonly Guid PseService = Guid.Parse("0000112F-0000-1000-8000-00805F9B34FB");

    public static readonly byte[] Target =
        { 0x79, 0x61, 0x35, 0xF0, 0xF0, 0xC5, 0x11, 0xD8, 0x09, 0x66, 0x08, 0x00, 0x20, 0x0C, 0x9A, 0x66 };

    public const string TypePhonebook = "x-bt/phonebook";
    public const string Contacts = "telecom/pb.vcf";
    public const string CombinedCallHistory = "telecom/cch.vcf";

    public const byte TagMaxListCount = 0x04, TagListStartOffset = 0x05, TagFilter = 0x06, TagFormat = 0x07, TagPhonebookSize = 0x08;

    // Bits do filtro de campos do vCard
    private const ulong Version = 1UL << 0, FormattedName = 1UL << 1, Name = 1UL << 2, Photo = 1UL << 3,
        Phone = 1UL << 7, Email = 1UL << 8, CallDateTime = (1UL << 27) | (1UL << 28);

    public const ulong FilterBasic = Version | FormattedName | Name | Phone | Email;
    public const ulong FilterWithPhoto = FilterBasic | Photo;
    public const ulong FilterCallHistory = Version | FormattedName | Name | Phone | CallDateTime;
}

public sealed class PbapClient
{
    private readonly ObexClient _obex;

    public PbapClient(ObexClient obex)
    {
        _obex = obex;
    }

    public Task ConnectAsync(CancellationToken ct = default) => _obex.ConnectAsync(PbapConstants.Target, ct);

    /// <summary>Quantos registros existem (sem baixar nenhum).</summary>
    public async Task<int> GetSizeAsync(string path, CancellationToken ct = default)
    {
        var result = await _obex.GetAsync(new[]
        {
            ObexHeader.Text(ObexHeader.Name, path),
            ObexHeader.Ascii(ObexHeader.Type, PbapConstants.TypePhonebook),
            new ObexHeader(ObexHeader.AppParameters, ObexAppParams.Build(
                (PbapConstants.TagMaxListCount, ObexAppParams.U16(0)))),
        }, ct);

        var parameters = ObexAppParams.Parse(result.Find(ObexHeader.AppParameters)?.Value);
        return parameters.TryGetValue(PbapConstants.TagPhonebookSize, out var size) ? ObexAppParams.ReadU16(size) : 0;
    }

    /// <summary>Baixa uma fatia da agenda como texto vCard 3.0.</summary>
    public async Task<string> PullAsync(string path, ulong filter, int max, int offset, CancellationToken ct = default)
    {
        var result = await _obex.GetAsync(new[]
        {
            ObexHeader.Text(ObexHeader.Name, path),
            ObexHeader.Ascii(ObexHeader.Type, PbapConstants.TypePhonebook),
            new ObexHeader(ObexHeader.AppParameters, ObexAppParams.Build(
                (PbapConstants.TagFilter, ObexAppParams.U64(filter)),
                (PbapConstants.TagFormat, ObexAppParams.U8(1)),
                (PbapConstants.TagMaxListCount, ObexAppParams.U16(max)),
                (PbapConstants.TagListStartOffset, ObexAppParams.U16(offset)))),
        }, ct);
        return Encoding.UTF8.GetString(result.Body);
    }
}

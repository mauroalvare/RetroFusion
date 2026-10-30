using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Retro.Core;

/// <summary>Versioned identity of an address-ordered code snapshot; never an identity of the source file.</summary>
public static class RoutineCodeIdentity
{
    public static string Compute(IEnumerable<(uint Address, byte[] Bytes)> instructions)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("RetroEmu-routine-v1"u8);
        Span<byte> header = stackalloc byte[8];
        foreach (var instruction in instructions.OrderBy(i => i.Address))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header, instruction.Address);
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)instruction.Bytes.Length);
            hash.AppendData(header); hash.AppendData(instruction.Bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

namespace Nes.Tests;

internal static class LegacyNesState
{
    public static byte[] FromCurrent(byte[] state, int version)
    {
        using var input = new BinaryReader(new MemoryStream(state));
        string magic = input.ReadString();
        Assert.That(input.ReadInt32(), Is.EqualTo(6));
        string identity = input.ReadString();
        int cpuStart = (int)input.BaseStream.Position;
        // Versions 2-5 stored the same 29-byte CPU payload, without the NMI poll latch.
        const int legacyCpuBytes = 29;
        int tailBytes = version < 4 ? 8 : version < 5 ? 4 : 0;
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(magic); writer.Write(version);
            if (version >= 3) writer.Write(identity);
            writer.Write(state.AsSpan(cpuStart, legacyCpuBytes));
            writer.Write(state.AsSpan(cpuStart + legacyCpuBytes + 1, state.Length - cpuStart - legacyCpuBytes - 1 - tailBytes));
        }
        return output.ToArray();
    }
}

using DEX.Core.Models;

namespace DEX.Core.Security;

public sealed record PeInfo(bool IsPe, ArchitectureKind? Architecture, string Machine);

public static class PeVerifier
{
    public static PeInfo Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 64)
            return new PeInfo(false, null, "too-small");
        if (reader.ReadUInt16() != 0x5A4D)
            return new PeInfo(false, null, "not-mz");
        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset > stream.Length - 6)
            return new PeInfo(false, null, "bad-offset");
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550)
            return new PeInfo(false, null, "not-pe");
        var machine = reader.ReadUInt16();
        var arch = machine switch
        {
            0x014C => ArchitectureKind.X86,
            0x8664 => ArchitectureKind.X64,
            0xAA64 => ArchitectureKind.Arm64,
            _ => (ArchitectureKind?)null
        };
        return new PeInfo(true, arch, $"0x{machine:X4}");
    }
}

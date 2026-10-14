namespace Se7en.DualSense.Hid;

/// <summary>
/// CRC32 IEEE (polynôme 0xEDB88320) utilisé par les rapports Bluetooth de la DualSense.
/// Le calcul commence par un octet « seed » : 0xA1 pour les rapports d'entrée, 0xA2 pour la sortie,
/// 0xA3 pour les rapports feature.
/// </summary>
public static class Crc32
{
    public const byte InputSeed = 0xA1;
    public const byte OutputSeed = 0xA2;
    public const byte FeatureSeed = 0xA3;

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(byte seed, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        crc = Table[(crc ^ seed) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}

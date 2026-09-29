using System.Text;

namespace CameraProbe;

internal sealed record ConVarEntry(string Name, short Type, ulong Flags, string Value,
    ulong Address, ulong NodeAddress, ulong NamePointer);

internal static class ConVarReader
{
    // Linked-list layout; differs from the older CCvar+0x40 pointer array.
    internal static ConVarEntry[] Find(GameReader reader)
    {
        ulong offset = ConVarLayout.ForBuild(reader.Build).InterfaceOffset;
        var tier0 = reader.Module("tier0.dll");
        if (offset + 0x80 > (ulong)tier0.Size) throw new InvalidOperationException("Cvar interface is outside tier0.dll.");
        ulong address = tier0.Address + offset;
        byte[] data = reader.Bytes(address, 0x80);
        ulong vtable = BitConverter.ToUInt64(data, 0);
        if (vtable < tier0.Address || vtable >= tier0.Address + (ulong)tier0.Size)
            throw new InvalidOperationException("Unexpected CCvar vtable.");
        ulong list = BitConverter.ToUInt64(data, 0x50);
        int count = BitConverter.ToUInt16(data, 0x5E);
        int capacity = BitConverter.ToInt32(data, 0x60);
        if (count is < 1 or > 30000 || capacity < count || capacity > 65535)
            throw new InvalidOperationException("Invalid cvar count or capacity.");
        ushort index = BitConverter.ToUInt16(data, 0x58), previous = ushort.MaxValue;
        var found = new List<ConVarEntry>();
        var visited = new HashSet<ushort>();
        while (index != ushort.MaxValue && visited.Count < count)
        {
            if (index >= capacity || !visited.Add(index)) throw new InvalidOperationException("Invalid/cyclic cvar list.");
            ulong nodeAddress = checked(list + (ulong)index * 16);
            byte[] node = reader.Bytes(nodeAddress, 16);
            if (BitConverter.ToUInt16(node, 8) != previous) throw new InvalidOperationException("Cvar list changed during traversal.");
            ulong variable = BitConverter.ToUInt64(node, 0);
            ulong namePointer = reader.Read<ulong>(variable);
            string name = Name(reader, namePointer);
            if (name is "mat_fullbright" or "spec_freeze_time")
            {
                short type = reader.Read<short>(variable + 0x28);
                _ = ConVarPolicy.Mask(name, type);
                ulong flags = reader.Read<ulong>(variable + 0x30);
                found.Add(new(name, type, flags, Value(reader, variable, type), variable, nodeAddress, namePointer));
            }
            previous = index;
            index = BitConverter.ToUInt16(node, 10);
        }
        if (index != ushort.MaxValue || visited.Count != count ||
            reader.Read<ulong>(address + 0x50) != list || reader.Read<ushort>(address + 0x5E) != count)
            throw new InvalidOperationException("Cvar registry changed; retry inspection.");
        if (found.Count != 2 || found.Select(e => e.Name).Distinct().Count() != 2)
            throw new InvalidOperationException("Both requested ConVars must resolve uniquely.");
        return found.OrderBy(e => e.Name, StringComparer.Ordinal).ToArray();
    }

    internal static ulong VerifyAndReadFlags(GameReader reader, ConVarEntry entry)
    {
        _ = ConVarPolicy.Mask(entry.Name, entry.Type);
        if (reader.Read<ulong>(entry.NodeAddress) != entry.Address ||
            reader.Read<ulong>(entry.Address) != entry.NamePointer || Name(reader, entry.NamePointer) != entry.Name ||
            reader.Read<short>(entry.Address + 0x28) != entry.Type)
            throw new InvalidOperationException($"Identity of {entry.Name} changed; refusing write.");
        return reader.Read<ulong>(entry.Address + 0x30);
    }

    private static string Value(GameReader reader, ulong address, short type)
    {
        // CVValue_t is aligned to 16 bytes in this SDK; values start at 0x60.
        if (type == 3) return reader.Read<int>(address + 0x60).ToString(System.Globalization.CultureInfo.InvariantCulture);
        float value = reader.Read<float>(address + 0x60);
        if (!float.IsFinite(value)) throw new InvalidOperationException("Invalid ConVar numeric value.");
        return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Name(GameReader reader, ulong pointer)
    {
        byte[] bytes = reader.Bytes(pointer, 64);
        int end = Array.IndexOf(bytes, (byte)0);
        return end < 0 ? "" : Encoding.ASCII.GetString(bytes, 0, end);
    }
}

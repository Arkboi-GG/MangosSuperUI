// WowDbcFile.cs — minimal WDBC reader for DBCs read straight out of the MPQs.
//
// PORTED FROM MSUIClient Formats/DbcReader.cs (DbcFile).
//
// This is deliberately separate from DbcService. DbcService reads DBCs that were
// EXTRACTED to a directory on disk and configured; the world-editor renderer
// reads them out of the client MPQs alongside the ADT that referenced them, so
// there is nothing to extract and nothing to configure.
//
// WDBC layout: a 20-byte header (magic, recordCount, fieldCount, recordSize,
// stringBlockSize), then fixed-size records, then the string block.

using System;
using System.Text;

namespace MangosSuperUI.Services;

public sealed class WowDbcFile
{
    public int RecordCount { get; private set; }
    public int FieldCount { get; private set; }
    public int RecordSize { get; private set; }

    private byte[] _records = Array.Empty<byte>();
    private byte[] _strings = Array.Empty<byte>();

    public static WowDbcFile? Parse(byte[] data)
    {
        if (data.Length < 20) return null;
        if (data[0] != 'W' || data[1] != 'D' || data[2] != 'B' || data[3] != 'C') return null;

        var dbc = new WowDbcFile
        {
            RecordCount = BitConverter.ToInt32(data, 4),
            FieldCount = BitConverter.ToInt32(data, 8),
            RecordSize = BitConverter.ToInt32(data, 12),
        };

        int stringSize = BitConverter.ToInt32(data, 16);
        if (dbc.RecordCount < 0 || dbc.RecordSize <= 0 || stringSize < 0) return null;

        long recordBytes = (long)dbc.RecordCount * dbc.RecordSize;
        if (20 + recordBytes + stringSize > data.Length) return null;

        dbc._records = new byte[recordBytes];
        Array.Copy(data, 20, dbc._records, 0, recordBytes);

        dbc._strings = new byte[stringSize];
        Array.Copy(data, 20 + recordBytes, dbc._strings, 0, stringSize);

        return dbc;
    }

    public uint GetUInt(int row, int field)
    {
        int offset = row * RecordSize + field * 4;
        if (row < 0 || row >= RecordCount || field < 0 || offset + 4 > _records.Length) return 0;
        return BitConverter.ToUInt32(_records, offset);
    }

    public int GetInt(int row, int field) => unchecked((int)GetUInt(row, field));

    /// <summary>
    /// Reinterpret a field's four bytes as a float. The Light tables need this:
    /// positions, falloff radii and every LightFloatBand value are IEEE floats
    /// sitting in the same fixed-width columns as the integers.
    /// </summary>
    public float GetFloat(int row, int field)
        => BitConverter.Int32BitsToSingle(GetInt(row, field));

    /// <summary>
    /// Read a stringref field, but ONLY when the stored value points at the
    /// START of a string in the block — offset 0 (the empty string), or a byte
    /// immediately preceded by a null. An INTEGER column misread as a stringref
    /// lands in the middle of a neighbouring string; when the block is packed
    /// with model paths, such a misread almost always still ends in ".mdl"/".mdx"
    /// — a convincing but truncated fake ("wFlo01.mdl" where the real path is
    /// "ElwGra01.mdl") that then fails to load. Returns null when the field is
    /// not a valid string start so a column scan can skip it.
    /// </summary>
    public string? GetStringIfStart(int row, int field)
    {
        uint offset = GetUInt(row, field);
        if (offset == 0) return "";
        if (offset >= _strings.Length) return null;
        if (_strings[offset - 1] != 0) return null;   // mid-string: not a real ref

        int end = (int)offset;
        while (end < _strings.Length && _strings[end] != 0) end++;

        return Encoding.UTF8.GetString(_strings, (int)offset, end - (int)offset);
    }
}

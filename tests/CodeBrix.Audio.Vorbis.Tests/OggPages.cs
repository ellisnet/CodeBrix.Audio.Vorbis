using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace CodeBrix.Audio.Vorbis.Tests;

/// <summary>A minimal Ogg page walker, so the tests check the container independently of any decoder.</summary>
internal static class OggPages
{
    /// <summary>One Ogg page header.</summary>
    /// <param name="HeaderType">The header-type flags (0x02 BOS, 0x04 EOS).</param>
    /// <param name="GranulePosition">The granule position.</param>
    /// <param name="SerialNumber">The logical-stream serial number.</param>
    /// <param name="SequenceNumber">The page sequence number.</param>
    internal readonly record struct Page(byte HeaderType, long GranulePosition, int SerialNumber, int SequenceNumber)
    {
        /// <summary>True on the first page of a logical stream.</summary>
        public bool IsBeginningOfStream => (HeaderType & 0x02) != 0;

        /// <summary>True on the last page of a logical stream.</summary>
        public bool IsEndOfStream => (HeaderType & 0x04) != 0;
    }

    /// <summary>Walks every page; throws if the bytes are not a clean sequence of Ogg pages.</summary>
    /// <param name="data">The stream bytes.</param>
    /// <returns>The pages in order.</returns>
    public static List<Page> Parse(byte[] data)
    {
        List<Page> pages = new List<Page>();
        int offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 27 || data[offset] != 'O' || data[offset + 1] != 'g' || data[offset + 2] != 'g' || data[offset + 3] != 'S')
            {
                throw new FormatException($"No Ogg page capture pattern at offset {offset}.");
            }

            ReadOnlySpan<byte> header = data.AsSpan(offset);
            byte type = header[5];
            long granule = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(6));
            int serial = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(14));
            int sequence = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(18));
            int segments = header[26];
            int body = 0;
            for (int i = 0; i < segments; i++) body += header[27 + i];

            pages.Add(new Page(type, granule, serial, sequence));
            offset += 27 + segments + body;
        }

        if (offset != data.Length) throw new FormatException("The last Ogg page runs past the end of the data.");
        return pages;
    }

    /// <summary>Reads the nominal bitrate field of the Vorbis identification header on the first page.</summary>
    /// <param name="data">The stream bytes.</param>
    /// <returns>The nominal bitrate, in bits per second (0 when the encoder left it unset).</returns>
    public static int NominalBitrate(byte[] data)
    {
        int segments = data[26];
        int packet = 27 + segments;

        // Identification header: type (1) "vorbis" (6) version (4) channels (1) rate (4) max (4) nominal (4) min (4).
        if (data[packet] != 1 || data[packet + 1] != 'v') throw new FormatException("The first page is not a Vorbis identification header.");
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(packet + 20));
    }
}

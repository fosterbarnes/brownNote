using System.Buffers.Binary;
using System.IO;
using Concentus;
using Concentus.Oggfile;
using Concentus.Structs;

namespace brownNote.Audio;

internal sealed class OpusAssetLoader
{
    private const int SampleRate = 48000;

    public OpusAsset Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new OpusAssetException($"Noise asset was not found: {path}");
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var head = ReadOpusHead(stream);
            stream.Position = 0;

            var decoder = OpusCodecFactory.CreateDecoder(SampleRate, head.Channels);
            var reader = new OpusOggReadStream(decoder, stream);
            try
            {
                var decoded = new List<short>();
                while (reader.HasNextPacket)
                {
                    var packet = reader.DecodeNextPacket();
                    if (packet is null)
                    {
                        throw new OpusAssetException(reader.LastError ?? "The Opus decoder returned no audio packet.");
                    }

                    decoded.AddRange(packet);
                }

                if (!string.IsNullOrWhiteSpace(reader.LastError))
                {
                    throw new OpusAssetException(reader.LastError);
                }

                return CreateAsset(decoded, reader.GranuleCount, head);
            }
            finally
            {
                reader.Close();
            }
        }
        catch (OpusAssetException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OpusAssetException($"Could not decode brown.opus: {exception.Message}", exception);
        }
    }

    private static OpusAsset CreateAsset(List<short> decoded, long granuleCount, OpusHead head)
    {
        if (granuleCount <= head.PreSkip)
        {
            throw new OpusAssetException("The Opus stream has no usable audio after its pre-skip.");
        }

        var logicalFrameCount = checked(granuleCount - head.PreSkip);
        var minimumFrameCount = (long)LoopingSampleProvider.LoopStartFrames +
                                (2L * LoopingSampleProvider.CrossfadeFrames);
        if (logicalFrameCount <= minimumFrameCount)
        {
            throw new OpusAssetException("The brown-noise asset is too short for the fixed loop geometry.");
        }

        var decodedFrameCount = decoded.Count / head.Channels;
        if (decoded.Count % head.Channels != 0 || decodedFrameCount < granuleCount)
        {
            throw new OpusAssetException("The decoded Opus samples do not match the stream's logical end.");
        }

        if (logicalFrameCount > int.MaxValue / head.Channels)
        {
            throw new OpusAssetException("The brown-noise asset is too large to keep in memory.");
        }

        var sampleCount = checked((int)(logicalFrameCount * head.Channels));
        var startSample = checked(head.PreSkip * head.Channels);
        var samples = new float[sampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = decoded[startSample + index] / 32768f;
        }

        return new OpusAsset(samples, head.Channels, checked((int)logicalFrameCount));
    }

    private static OpusHead ReadOpusHead(Stream stream)
    {
        stream.Position = 0;
        var firstSerial = (int?)null;
        var firstPacket = new MemoryStream();
        var firstPacketComplete = false;
        var firstPage = true;

        while (stream.Position < stream.Length)
        {
            var header = ReadRequired(stream, 27, "an incomplete Ogg page header");
            if (!header.AsSpan(0, 4).SequenceEqual("OggS"u8))
            {
                throw new OpusAssetException("The asset is not a valid Ogg Opus stream.");
            }

            if (header[4] != 0)
            {
                throw new OpusAssetException("The Ogg stream uses an unsupported bitstream version.");
            }

            var headerType = header[5];
            var serial = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(14, 4));
            if (firstPage)
            {
                if ((headerType & 0x02) == 0 || (headerType & 0x01) != 0)
                {
                    throw new OpusAssetException("The Opus stream does not begin with a valid Ogg packet.");
                }

                firstSerial = serial;
                firstPage = false;
            }
            else if (serial != firstSerial)
            {
                throw new OpusAssetException("Multi-stream Ogg assets are not supported.");
            }

            var segmentTable = ReadRequired(stream, header[26], "an incomplete Ogg segment table");
            var bodyLength = segmentTable.Sum(segment => segment);
            var body = ReadRequired(stream, bodyLength, "an incomplete Ogg page body");

            if (firstPacketComplete)
            {
                continue;
            }

            if ((headerType & 0x01) != 0 && firstPacket.Length == 0)
            {
                throw new OpusAssetException("The first Ogg packet is incomplete.");
            }

            var bodyOffset = 0;
            foreach (var segmentLength in segmentTable)
            {
                firstPacket.Write(body, bodyOffset, segmentLength);
                bodyOffset += segmentLength;
                if (segmentLength < byte.MaxValue)
                {
                    firstPacketComplete = true;
                    break;
                }
            }
        }

        if (!firstPacketComplete)
        {
            throw new OpusAssetException("The Ogg stream ended before its Opus header was complete.");
        }

        var packet = firstPacket.ToArray();
        if (packet.Length < 19 || !packet.AsSpan(0, 8).SequenceEqual("OpusHead"u8))
        {
            throw new OpusAssetException("The Ogg stream does not contain an OpusHead packet.");
        }

        if (packet[8] >= 16)
        {
            throw new OpusAssetException("The Opus header uses an unsupported version.");
        }

        var channels = packet[9];
        if (channels is not (1 or 2))
        {
            throw new OpusAssetException("The asset must contain one or two Opus channels.");
        }

        var preSkip = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10, 2));
        var outputGain = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(16, 2));
        if (outputGain != 0)
        {
            throw new OpusAssetException("The Opus asset has unsupported output gain metadata.");
        }

        if (packet[18] != 0 || packet.Length != 19)
        {
            throw new OpusAssetException("The Opus asset uses an unsupported channel mapping.");
        }

        return new OpusHead(channels, preSkip);
    }

    private static byte[] ReadRequired(Stream stream, int count, string description)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new OpusAssetException($"The Ogg stream contains {description}.");
            }

            offset += read;
        }

        return buffer;
    }

    private readonly record struct OpusHead(int Channels, int PreSkip);
}

internal sealed class OpusAssetException : Exception
{
    public OpusAssetException(string message)
        : base(message)
    {
    }

    public OpusAssetException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed record OpusAsset(float[] Samples, int Channels, int FrameCount);

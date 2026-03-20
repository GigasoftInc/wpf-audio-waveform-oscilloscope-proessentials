using System;
using System.IO;

namespace AudioWaveform
{
    /// <summary>
    /// Lightweight 16-bit stereo WAV file parser.
    ///
    /// Supports: PCM, 16-bit, 1 or 2 channels, any sample rate.
    /// Does not support: MP3, 24-bit, 32-bit float, compressed formats.
    ///
    /// Export settings from Audacity for compatibility:
    ///   Format    : WAV (Microsoft)
    ///   Encoding  : Signed 16-bit PCM
    ///   Channels  : Stereo
    ///   Sample Rate: 16000 Hz (keeps file size small, sufficient for waveform display)
    ///   Header    : Legacy (not RF64, not multi-chunk)
    ///   Metadata  : Clear all (Edit → Metadata Editor → Clear) before export
    /// </summary>
    public class WaveReader
    {
        public Int32[][] Data;
        public int CompressionCode;
        public int NumberOfChannels;
        public int SampleRate;
        public int AverageBytesPerSecond;
        public int SignificantBitsPerSample;
        public int BlockAlign;
        public int Frames;
        public double TimeLength;

        public WaveReader(Stream stream)
        {
            BinaryReader br = new BinaryReader(stream);
            try
            {
                if (new string(br.ReadChars(4)).ToUpper() == "RIFF")
                {
                    int length = br.ReadInt32();
                    if (new string(br.ReadChars(4)).ToUpper() == "WAVE")
                    {
                        // Read fmt chunk
                        string chunkName   = new string(br.ReadChars(4));
                        int    chunkLength = br.ReadInt32();

                        this.CompressionCode        = br.ReadInt16();
                        this.NumberOfChannels       = br.ReadInt16();
                        this.SampleRate             = br.ReadInt32();
                        this.AverageBytesPerSecond  = br.ReadInt32();
                        this.BlockAlign             = br.ReadInt16();
                        this.SignificantBitsPerSample = br.ReadInt16();

                        // Skip any extra fmt bytes
                        if (chunkLength > 16)
                            br.ReadBytes(chunkLength - 16);

                        // Scan for data chunk — skip any non-data chunks
                        // (metadata, LIST, INFO etc that Suno/Audacity may embed)
                        chunkName = new string(br.ReadChars(4));
                        while (chunkName.ToLower() != "data")
                        {
                            int skipLen = br.ReadInt32();
                            br.ReadBytes(skipLen);
                            chunkName = new string(br.ReadChars(4));
                        }

                        chunkLength = br.ReadInt32();

                        // Calculate frame count from actual data chunk size
                        // Formula: bytes / (bits/8) / channels
                        this.Frames     = chunkLength / (this.SignificantBitsPerSample / 8) / this.NumberOfChannels;
                        this.TimeLength = (double)this.Frames / (double)this.SampleRate;

                        // Allocate per-channel arrays
                        this.Data = new Int32[this.NumberOfChannels][];
                        for (int j = 0; j < this.NumberOfChannels; j++)
                            this.Data[j] = new Int32[this.Frames];

                        // Read interleaved samples: L R L R L R ...
                        for (int i = 0; i < this.Frames; i++)
                            for (int j = 0; j < this.NumberOfChannels; j++)
                                this.Data[j][i] = br.ReadInt16();
                    }
                    else
                        throw new Exception("Not a WAVE file");
                }
                else
                    throw new Exception("Not a RIFF file");
            }
            finally
            {
                br.Close();
            }
        }
    }
}

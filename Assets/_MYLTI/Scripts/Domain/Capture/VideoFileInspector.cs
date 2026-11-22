using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Mylti.Domain
{
    /// <summary>
    /// Reads container metadata (no decoding) to verify a recording really exists and how long it is.
    /// Supports MP4/MOV (mvhd/mehd/mdhd/hdlr/stts) and Matroska/WebM (Info.Duration, Tracks).
    /// Fragmented MP4 without mehd and live MKV without Duration → DurationSeconds = null (unknown).
    /// </summary>
    public static class VideoFileInspector
    {
        public static VideoInfo Inspect(string path)
        {
            var info = new VideoInfo { Path = path };
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    info.Exists = false;
                    info.Error = "File not found";
                    return info;
                }
                var fi = new FileInfo(path);
                info.Exists = true;
                info.SizeBytes = fi.Length;
                if (fi.Length < 32) { info.Error = "File too small to be a video"; return info; }

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var head = new byte[12];
                    fs.Read(head, 0, head.Length);
                    fs.Position = 0;
                    if (head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3)
                    {
                        info.Container = "matroska";
                        Mkv.Parse(fs, info);
                    }
                    else if (Encoding.ASCII.GetString(head, 4, 4) is string t && (t == "ftyp" || t == "moov" || t == "mdat" || t == "free" || t == "wide" || t == "skip"))
                    {
                        info.Container = "mp4";
                        Mp4.Parse(fs, info);
                    }
                    else
                    {
                        info.Container = "unknown";
                        info.Error = "Unsupported container (use MP4 or MKV)";
                    }
                }
            }
            catch (IOException e) { info.Error = "File is locked or unreadable (still recording?): " + e.Message; }
            catch (UnauthorizedAccessException e) { info.Error = "Access denied: " + e.Message; }
            catch (Exception e) { info.Error = "Could not parse container: " + e.Message; }
            return info;
        }

        /// <summary>Map inspection result to a capture status. Never returns CaptureVerified without a real duration.</summary>
        public static CaptureStatus Classify(VideoInfo v, double target, double tolerance, out string message)
        {
            if (v == null || !v.Exists) { message = "Video file missing."; return CaptureStatus.VideoMissing; }
            if (!v.DurationSeconds.HasValue)
            {
                message = "Duration unknown: " + (v.Error ?? "container has no duration") + ". Tip: in OBS use File ▸ Remux Recordings to MP4.";
                return CaptureStatus.DurationUnknown;
            }
            double d = v.DurationSeconds.Value;
            if (v.HasVideoTrack == false) { message = "File has no video track."; return CaptureStatus.Failed; }
            if (Math.Abs(d - target) > tolerance)
            {
                message = $"Duration {d:0.00}s is outside {target - tolerance:0}–{target + tolerance:0}s. Trim/extend in post with real footage or re-record.";
                return CaptureStatus.InvalidDuration;
            }
            message = $"Video verified: {d:0.00}s.";
            return CaptureStatus.CaptureVerified;
        }

        public static string FindNewestVideo(string folder, DateTime notBeforeUtc)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
            string[] exts = { ".mp4", ".mkv", ".mov" };
            return new DirectoryInfo(folder).GetFiles()
                .Where(f => exts.Contains(f.Extension.ToLowerInvariant()) && f.LastWriteTimeUtc >= notBeforeUtc)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }

        // ----------------------------------------------------------------- MP4
        static class Mp4
        {
            public static void Parse(Stream s, VideoInfo info)
            {
                long end = s.Length;
                bool foundMoov = false;
                double? mvhdDur = null, mehdDur = null;
                uint movieTimescale = 0;
                var tracks = new List<(string handler, uint timescale, ulong duration, long samples)>();

                foreach (var box in Boxes(s, 0, end))
                {
                    if (box.type != "moov") continue;
                    foundMoov = true;
                    foreach (var b in Boxes(s, box.dataStart, box.end))
                    {
                        if (b.type == "mvhd")
                        {
                            s.Position = b.dataStart;
                            int version = s.ReadByte(); Skip(s, 3);
                            if (version == 1) { Skip(s, 16); movieTimescale = ReadU32(s); ulong dur = ReadU64(s); if (movieTimescale > 0 && dur > 0 && dur != ulong.MaxValue) mvhdDur = (double)dur / movieTimescale; }
                            else { Skip(s, 8); movieTimescale = ReadU32(s); uint dur = ReadU32(s); if (movieTimescale > 0 && dur > 0 && dur != uint.MaxValue) mvhdDur = (double)dur / movieTimescale; }
                        }
                        else if (b.type == "mvex")
                        {
                            foreach (var m in Boxes(s, b.dataStart, b.end))
                            {
                                if (m.type != "mehd") continue;
                                s.Position = m.dataStart;
                                int version = s.ReadByte(); Skip(s, 3);
                                ulong fd = version == 1 ? ReadU64(s) : ReadU32(s);
                                if (movieTimescale > 0 && fd > 0) mehdDur = (double)fd / movieTimescale;
                            }
                        }
                        else if (b.type == "trak")
                        {
                            tracks.Add(ParseTrak(s, b.dataStart, b.end));
                        }
                    }
                }
                if (!foundMoov) { info.Error = "No 'moov' box — the recording was not finalised (recorder still running or crashed)."; return; }
                info.DurationSeconds = mvhdDur ?? mehdDur;
                info.HasVideoTrack = tracks.Any(t => t.handler == "vide");
                info.HasAudioTrack = tracks.Any(t => t.handler == "soun");
                var v = tracks.FirstOrDefault(t => t.handler == "vide");
                if (v.handler == "vide" && v.samples > 0)
                {
                    info.VideoFrameCount = v.samples;
                    if (v.timescale > 0 && v.duration > 0) info.VideoFps = Math.Round(v.samples / ((double)v.duration / v.timescale), 3);
                }
                if (!info.DurationSeconds.HasValue) info.Error = "Fragmented MP4 without total duration.";
            }

            static (string, uint, ulong, long) ParseTrak(Stream s, long start, long end)
            {
                string handler = null; uint ts = 0; ulong dur = 0; long samples = 0;
                foreach (var b in Boxes(s, start, end))
                {
                    if (b.type != "mdia") continue;
                    foreach (var m in Boxes(s, b.dataStart, b.end))
                    {
                        if (m.type == "mdhd")
                        {
                            s.Position = m.dataStart;
                            int version = s.ReadByte(); Skip(s, 3);
                            if (version == 1) { Skip(s, 16); ts = ReadU32(s); dur = ReadU64(s); }
                            else { Skip(s, 8); ts = ReadU32(s); dur = ReadU32(s); }
                        }
                        else if (m.type == "hdlr")
                        {
                            s.Position = m.dataStart + 8; // version/flags + pre_defined
                            var buf = new byte[4]; s.Read(buf, 0, 4);
                            handler = Encoding.ASCII.GetString(buf);
                        }
                        else if (m.type == "minf")
                        {
                            foreach (var n in Boxes(s, m.dataStart, m.end))
                            {
                                if (n.type != "stbl") continue;
                                foreach (var st in Boxes(s, n.dataStart, n.end))
                                {
                                    if (st.type != "stts") continue;
                                    s.Position = st.dataStart + 4;
                                    uint entries = ReadU32(s);
                                    for (uint i = 0; i < entries && s.Position + 8 <= st.end; i++) { samples += ReadU32(s); ReadU32(s); }
                                }
                            }
                        }
                    }
                }
                return (handler, ts, dur, samples);
            }

            static IEnumerable<(string type, long dataStart, long end)> Boxes(Stream s, long start, long end)
            {
                long pos = start;
                var hdr = new byte[8];
                while (pos + 8 <= end)
                {
                    s.Position = pos;
                    if (s.Read(hdr, 0, 8) < 8) yield break;
                    ulong size = ((ulong)hdr[0] << 24) | ((ulong)hdr[1] << 16) | ((ulong)hdr[2] << 8) | hdr[3];
                    string type = Encoding.ASCII.GetString(hdr, 4, 4);
                    long headerLen = 8;
                    if (size == 1) { size = ReadU64(s); headerLen = 16; }
                    else if (size == 0) size = (ulong)(end - pos);
                    if (size < (ulong)headerLen || pos + (long)size > end) yield break; // truncated file
                    yield return (type, pos + headerLen, pos + (long)size);
                    pos += (long)size;
                }
            }
        }

        // ----------------------------------------------------------------- MKV
        static class Mkv
        {
            const uint Segment = 0x18538067, Info = 0x1549A966, TimecodeScale = 0x2AD7B1, Duration = 0x4489,
                       Tracks = 0x1654AE6B, TrackEntry = 0xAE, TrackType = 0x83, Cluster = 0x1F43B675;

            public static void Parse(Stream s, VideoInfo info)
            {
                long len = s.Length;
                s.Position = 0;
                // EBML header
                ReadId(s); long hsize = ReadSize(s, out _); s.Position += hsize;
                uint id = ReadId(s);
                if (id != Segment) { info.Error = "No Matroska Segment"; return; }
                long segSize = ReadSize(s, out bool unknown);
                long segStart = s.Position;
                long segEnd = unknown ? len : Math.Min(len, segStart + segSize);
                double scale = 1_000_000;
                double? dur = null;
                bool video = false, audio = false;
                while (s.Position < segEnd - 2)
                {
                    long elemStart = s.Position;
                    uint eid = ReadId(s);
                    long esize = ReadSize(s, out bool eUnknown);
                    long dataStart = s.Position;
                    if (eid == Cluster) { if (eUnknown) break; s.Position = dataStart + esize; continue; }
                    if (eUnknown) break;
                    long dataEnd = Math.Min(segEnd, dataStart + esize);
                    if (eid == Info)
                    {
                        while (s.Position < dataEnd)
                        {
                            uint cid = ReadId(s); long cs = ReadSize(s, out _); long cstart = s.Position;
                            if (cid == TimecodeScale) scale = ReadUInt(s, (int)cs);
                            else if (cid == Duration) dur = ReadFloat(s, (int)cs);
                            s.Position = cstart + cs;
                        }
                    }
                    else if (eid == Tracks)
                    {
                        while (s.Position < dataEnd)
                        {
                            uint cid = ReadId(s); long cs = ReadSize(s, out _); long cstart = s.Position;
                            if (cid == TrackEntry)
                            {
                                while (s.Position < cstart + cs)
                                {
                                    uint tid = ReadId(s); long ts = ReadSize(s, out _); long tstart = s.Position;
                                    if (tid == TrackType)
                                    {
                                        ulong type = ReadUInt(s, (int)ts);
                                        if (type == 1) video = true;
                                        if (type == 2) audio = true;
                                    }
                                    s.Position = tstart + ts;
                                }
                            }
                            s.Position = cstart + cs;
                        }
                    }
                    s.Position = dataEnd;
                    if (s.Position <= elemStart) break;
                }
                info.HasVideoTrack = video;
                info.HasAudioTrack = audio;
                if (dur.HasValue && dur.Value > 0) info.DurationSeconds = dur.Value * scale / 1e9;
                else info.Error = "MKV has no Duration (recording not finalised).";
            }

            static uint ReadId(Stream s)
            {
                int b = s.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                int len = b >= 0x80 ? 1 : b >= 0x40 ? 2 : b >= 0x20 ? 3 : b >= 0x10 ? 4 : 0;
                if (len == 0) throw new InvalidDataException("Bad EBML id");
                uint v = (uint)b;
                for (int i = 1; i < len; i++) v = (v << 8) | (uint)s.ReadByte();
                return v;
            }

            static long ReadSize(Stream s, out bool unknown)
            {
                int b = s.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                int len = 1; int mask = 0x80;
                while (len <= 8 && (b & mask) == 0) { len++; mask >>= 1; }
                if (len > 8) throw new InvalidDataException("Bad EBML size");
                long v = b & (mask - 1);
                bool allOnes = v == mask - 1;
                for (int i = 1; i < len; i++) { int n = s.ReadByte(); v = (v << 8) | (uint)n; if (n != 0xFF) allOnes = false; }
                unknown = allOnes;
                return v;
            }

            static ulong ReadUInt(Stream s, int n)
            {
                ulong v = 0;
                for (int i = 0; i < n; i++) v = (v << 8) | (uint)s.ReadByte();
                return v;
            }

            static double ReadFloat(Stream s, int n)
            {
                var b = new byte[n];
                s.Read(b, 0, n);
                if (BitConverter.IsLittleEndian) Array.Reverse(b);
                return n == 4 ? BitConverter.ToSingle(b, 0) : n == 8 ? BitConverter.ToDouble(b, 0) : 0;
            }
        }

        // ------------------------------------------------------------- helpers
        static void Skip(Stream s, long n) => s.Position += n;

        static uint ReadU32(Stream s)
        {
            var b = new byte[4];
            if (s.Read(b, 0, 4) < 4) throw new EndOfStreamException();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        static ulong ReadU64(Stream s) => ((ulong)ReadU32(s) << 32) | ReadU32(s);
    }
}

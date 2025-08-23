using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using System.Diagnostics;

namespace GameRes
{
    public class VideoMetaData
    {
        /// <summary>Video width in pixels.</summary>
        public uint Width { get; set; }

        /// <summary>Video height in pixels.</summary>
        public uint Height { get; set; }

        /// <summary>Video duration in milliseconds.</summary>
        public long Duration { get; set; }

        /// <summary>Video frame rate (frames per second).</summary>
        public double FrameRate { get; set; }

        /// <summary>Video bitrate in bits per second.</summary>
        public int BitRate { get; set; }

        /// <summary>Video codec name.</summary>
        public string Codec { get; set; }

        /// <summary>Audio codec name, if present.</summary>
        public string AudioCodec { get; set; }

        /// <summary>Video source file name, if any.</summary>
        public string FileName { get; set; }

        /// <summary>Whether the video has audio.</summary>
        public bool HasAudio { get; set; }

        public string CommonExtension { get; set; }

        public int iWidth { get { return (int)Width; } }
        public int iHeight { get { return (int)Height; } }
    }

    public class VideoEntry : Entry
    {
        public override string Type { get { return "video"; } }
    }

    public class VideoData : IDisposable
    {
        private   Stream m_stream = null;
        private string m_tempFile = null;

        public uint          Width { get; private set; }
        public uint         Height { get; private set; }
        public long       Duration { get; private set; }
        public double    FrameRate { get; private set; }
        public string        Codec { get; private set; }
        public bool       HasAudio { get; private set; }
        public string     FileName { get; set; }
        public string     TempFile { get { return m_tempFile; } }
        public Stream       Stream { get { return m_stream; } }

        public VideoData(VideoMetaData meta)
        {
            Width     = meta.Width;
            Height    = meta.Height;
            Duration  = meta.Duration;
            FrameRate = meta.FrameRate;
            Codec     = meta.Codec;
            HasAudio  = meta.HasAudio;
            FileName  = meta.FileName;

            //Trace.WriteLine($"{FileName}/{m_tempFile ?? "non-temp"} created", "[VideoData]");
        }

        public VideoData (Stream stream, VideoMetaData meta, bool needsTempFile = false) : this (meta)
        {
            
            if (needsTempFile)
            {
                m_tempFile = Path.Combine(
                    Path.GetTempPath(), 
                    $"garbro_{Guid.NewGuid()}.{meta.CommonExtension ?? "mp4"}"
                );
                using (var fileStream = File.Create(m_tempFile))
                {
                    stream.Position = 0;
                    stream.CopyTo(fileStream);
                }
                m_stream = new FileStream(m_tempFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                return;
            }
            m_stream = stream;
        }

        public void Dispose()
        {
            //Trace.WriteLine($"{FileName}/{m_tempFile ?? "non-temp"} disposed", "[VideoData]");

            if (m_stream != null)
            {
                m_stream.Dispose();
                m_stream = null;
            }

            if (!string.IsNullOrEmpty (m_tempFile) && File.Exists (m_tempFile))
            {
                try
                {
                    File.Delete (m_tempFile);
                    m_tempFile = null;
                }
                catch { }
            }
        }
    }

    public abstract class VideoFormat : IResource
    {
        public override string Type { get { return "video"; } }

        public abstract VideoMetaData ReadMetaData (IBinaryStream file);

        public abstract VideoData Read (IBinaryStream file, VideoMetaData info);

        public static VideoData Read (IBinaryStream file)
        {
            var format = FindFormat (file);
            if (null == format)
                return null;

            file.Position = 0;
            return format.Item1.Read (file, format.Item2);
        }

        public static System.Tuple<VideoFormat, VideoMetaData> FindFormat (IBinaryStream file)
        {
            foreach (var impl in FormatCatalog.Instance.FindFormats<VideoFormat>(file.Name, file.Signature))
            {
                try
                {
                    file.Position = 0;
                    VideoMetaData metadata = impl.ReadMetaData (file);
                    if (null != metadata)
                    {
                        metadata.FileName = file.Name;
                        return Tuple.Create (impl, metadata);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }
            return null;
        }

        public bool IsBuiltin
        {
            get { return this.GetType().Assembly == typeof (VideoFormat).Assembly; }
        }

        public static VideoFormat FindByTag (string tag)
        {
            return FormatCatalog.Instance.VideoFormats.FirstOrDefault (x => x.Tag == tag);
        }
    }
}
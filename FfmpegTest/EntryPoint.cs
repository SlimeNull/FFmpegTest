using Sdcb.FFmpeg;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Toolboxs.Generators;
using Sdcb.FFmpeg.Utils;
using SkiaSharp;

namespace FFmpegTest
{
    public class EntryPoint
    {
        static unsafe void Main(string[] args)
        {

            using FormatContext formatContext = FormatContext.AllocOutput(formatName: "mp4");
            formatContext.VideoCodec = Codec.CommonEncoders.Libx264;
            formatContext.AudioCodec = Codec.CommonEncoders.AAC;

            using CodecContext videoEncoder = new CodecContext(formatContext.VideoCodec)
            {
                Width = 800,
                Height = 600,
                Framerate = new AVRational(1, 30),
                TimeBase = new AVRational(1, 30),
                PixelFormat = AVPixelFormat.Yuv420p,
                Flags = AV_CODEC_FLAG.GlobalHeader
            };

            AVChannelLayout avChannelLayout = default;
            ffmpeg.av_channel_layout_default(&avChannelLayout, 1);

            using CodecContext audioEncoder = new CodecContext(formatContext.AudioCodec)
            {
                BitRate = 320000,
                SampleFormat = AVSampleFormat.Flt,
                SampleRate = 44100,
                ChLayout = avChannelLayout,
                CodecType = AVMediaType.Audio,
                FrameSize = 1024,
                TimeBase = new AVRational(1, 44100)
            };

            MediaStream videoStream = formatContext.NewStream(formatContext.VideoCodec);
            MediaStream audioStream = formatContext.NewStream(formatContext.AudioCodec);

            videoEncoder.Open(formatContext.VideoCodec);
            audioEncoder.Open(formatContext.AudioCodec, new MediaDictionary()
            {
                ["frame_size"] = "1024"
            });


            videoStream.Codecpar!.CopyFrom(videoEncoder);
            videoStream.TimeBase = videoEncoder.TimeBase;

            audioStream.Codecpar!.CopyFrom(audioEncoder);
            audioStream.TimeBase = audioEncoder.TimeBase;


            string outputPath = "output.mp4";
            formatContext.DumpFormat(streamIndex: 0, outputPath, isOutput: true);

            using IOContext ioc = IOContext.OpenWrite(outputPath);
            formatContext.Pb = ioc;

            SKBitmap bitmap = new SKBitmap(800, 600, SKColorType.Rgba8888, SKAlphaType.Opaque);
            SKCanvas canvas = new SKCanvas(bitmap);
            SKPaint paint = new SKPaint();

            VideoFrameConverter frameConverter = new VideoFrameConverter();

            formatContext.WriteHeader();

            var seconds = 20;
            using var packetRef = new Packet();

            // video
            for (int i = 0; i < 30 * seconds; i++)
            {
                var lightness = (i % 255) / 255f;

                paint.SetColor(new SKColorF(lightness, lightness, lightness), bitmap.ColorSpace);

                canvas.DrawRect(new SKRect(0, 0, 800, 600), paint);
                canvas.Flush();

                using var frame = new Frame();
                frame.Width = 800;
                frame.Height = 600;
                frame.Format = (int)AVPixelFormat.Rgba;
                frame.Data[0] = bitmap.GetPixels();
                frame.Linesize[0] = bitmap.RowBytes;
                frame.Pts = i;

                using var convertedFrame = videoEncoder.CreateFrame();
                convertedFrame.MakeWritable();
                frameConverter.ConvertFrame(frame, convertedFrame);
                convertedFrame.Pts = i;

                foreach (var packet in videoEncoder.EncodeFrame(convertedFrame, packetRef))
                {
                    packet.RescaleTimestamp(videoEncoder.TimeBase, videoStream.TimeBase);
                    packet.StreamIndex = videoStream.Index;


                    formatContext.WritePacket(packet);
                }
            }

            foreach (var packet in videoEncoder.EncodeFrame(null, packetRef))
            {
                packet.RescaleTimestamp(videoEncoder.TimeBase, videoStream.TimeBase);
                packet.StreamIndex = videoStream.Index;


                formatContext.WritePacket(packet);
            }



            #region Audio
            // audio
            var samples = new float[seconds * 44100];
            var maxSampleCount = audioEncoder.FrameSize;
            var audioScale = 0.1f;
            var frequency = 2000;
            var random = new Random();

            for (int i = 0; i < samples.Length; i++)
            {
                var sinValue = MathF.Sin(i / 44100f * frequency);
                //var noiseValue = random.NextSingle();
                var squareValue = MathF.Round(sinValue);
                var finalValue = squareValue;
                samples[i] = finalValue * audioScale;
            }

            fixed (float* samplePtr = samples)
            {
                for (int i = 0; i < samples.Length;)
                {
                    int frameSampleCount = (int)maxSampleCount;
                    if (frameSampleCount + i >= samples.Length)
                    {
                        // ignore now
                        break;
                    }

                    var frame = new Frame();
                    frame.Format = (int)AVSampleFormat.Flt;
                    frame.NbSamples = frameSampleCount;
                    frame.ChLayout = audioEncoder.ChLayout;
                    frame.SampleRate = 44100;

                    frame.Data[0] = (nint)(void*)(samplePtr + i);
                    frame.Pts = i;

                    foreach (var packet in audioEncoder.EncodeFrame(frame, packetRef))
                    {
                        packet.RescaleTimestamp(audioEncoder.TimeBase, audioStream.TimeBase);
                        packet.StreamIndex = audioStream.Index;

                        formatContext.WritePacket(packet);
                    }

                    i += frameSampleCount;
                }
            }

            #endregion

            formatContext.WriteTrailer();

            //VideoFrameGenerator.Yuv420pSequence(videoEncoder.Width, videoEncoder.Height, 600)
            //    .ConvertFrames(videoEncoder)
            //    .EncodeAllFrames(fc, null, videoEncoder)
            //    .WriteAll(fc);

            //fc.WriteTrailer();
        }
    }
}

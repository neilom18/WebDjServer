using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SIPSorcery.Net;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Org.BouncyCastle.Crypto.Generators;
using SIPSorcery.Sys;
using Signaler.Models;
using System.Collections.Generic;

namespace Signaler
{
    public class Mixer
    {
        private readonly ILogger<Mixer> _logger;

        private Stream _audioBuffer;
        private ConcurrentQueue<RTPPacket> _queue;
        private ConcurrentQueue<AudioData> _queue2;
        private System.Timers.Timer _timer;
        private uint _timestamp;
        private byte[] _bytes;

        public Mixer(ILogger<Mixer> logger)
        {
            _logger = logger;
            _audioBuffer = new MemoryStream();
            _queue = new ConcurrentQueue<RTPPacket>();
            _queue2 = new ConcurrentQueue<AudioData>();
            _bytes = new byte[1024 * 1024];
            _timer = new System.Timers.Timer(20);
            _timestamp = Crypto.GetRandomUInt();
        }

        /// <summary>
        ///     Adiciona um pacote ao buffer
        /// </summary>
        public void AddRawPacket(byte[] pkt)
        {
            _audioBuffer.Write(pkt, 0, pkt.Length);
        }

        public void AddRawPacket(RTPPacket pk)
        {
            _queue.Enqueue(pk);
            if (_queue.Count > 10)
                ProcessRTPPacket();
        }

        public void AddRawPacket(RTPPacket pk, List<User> users)
        {
            _queue2.Enqueue(new AudioData {Packet = pk ,Listeners = users });
        }


        public void StartAudioProcess()
        {
            _timer.AutoReset = false;
            _timer.Elapsed += Timer_Elapsed;
            _timer.Start();
            //Task.Run(ProcessAudio);
            Task.Run(ProcessRTPPacket);
        }

        public void ProcessRTPPackets3()
        {
            if (_queue2.IsEmpty) return;
            while (_queue2.TryDequeue(out var audioData))
            {
                foreach (var receiverUser in audioData.Listeners)
                {
                    receiverUser.PeerConnection?.SendRtpRaw
                            (SDPMediaTypesEnum.audio,
                            audioData.Packet.Payload,
                            _timestamp,
                            audioData.Packet.Header.MarkerBit,
                            audioData.Packet.Header.PayloadType);
                }
                _timestamp += 160;
            }
        }

        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            ProcessRTPPackets3();
            _timer.Start();
        }



        private void ProcessRTPPacket()
        {



            while (true)
            {
                Task.Delay(100).Wait();
                if (_queue.Count >= 5)
                {
                    ProcessRTPPacket2();
                }
            }
        }

        private void ProcessRTPPacket2()
        {
            if (_queue.IsEmpty) return;
            try
            {
                using var tmpStream = new MemoryStream();
                while (_queue.TryDequeue(out var pkt))
                {
                    tmpStream.Write(pkt.Payload, 0, 160);
                }

                tmpStream.Seek(0, SeekOrigin.Begin);
                _bytes = tmpStream.ToArray();
                HasAudioData.Invoke(this, new TesteEventArgs { bytes = _bytes });
                /*var waveFormat = WaveFormat.CreateMuLawFormat(8000, 1);
                        var reader = new RawSourceWaveStream(tmpStream, waveFormat);
                        using var convertedStream = WaveFormatConversionStream.CreatePcmStream(reader);
                        WaveFileWriter.CreateWaveFile(@"C:\\temp\\wavs\\teste5.wav", convertedStream);*/
            }
            catch (Exception e)
            {
            }
        }

        public EventHandler<TesteEventArgs> HasAudioData;

        public class TesteEventArgs : EventArgs
        {
            public byte[] bytes { get; set; }
            public uint Timestamp { get; set; }
        }


        private void ProcessAudio()
        {
            while (true)
            {
                if (_audioBuffer.Length > 0)
                {
                    var waveFormat = WaveFormat.CreateMuLawFormat(8000, 1);
                    using var tmpMemStream = new MemoryStream((_audioBuffer as MemoryStream).ToArray());
                    var reader = new RawSourceWaveStream(tmpMemStream, waveFormat);
                    using var convertedStream = WaveFormatConversionStream.CreatePcmStream(reader);
                    WaveFileWriter.CreateWaveFile(@"C:\\temp\\wavs\\teste5.wav", convertedStream);
                }
            }
        }

        public static byte[] ReadFully(Stream input)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
        }
    }

    public class RawSourceWaveStream : WaveStream
    {
        private Stream sourceStream;
        private WaveFormat waveFormat;

        public RawSourceWaveStream(Stream sourceStream, WaveFormat waveFormat)
        {
            this.sourceStream = sourceStream;
            this.waveFormat = waveFormat;
        }

        public override WaveFormat WaveFormat
        {
            get { return this.waveFormat; }
        }

        public override long Length
        {
            get { return this.sourceStream.Length; }
        }

        public override long Position
        {
            get
            {
                return this.sourceStream.Position;
            }
            set
            {
                this.sourceStream.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return sourceStream.Read(buffer, offset, count);
        }
    }
}

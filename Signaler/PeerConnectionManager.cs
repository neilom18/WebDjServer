﻿using Concentus.Oggfile;
using Concentus.Structs;
using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Signaler.Hubs;
using Signaler.Models;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using static Signaler.Mixer;

namespace Signaler
{
    /*
       Instantiate the RTCPeerConnection instance,
       Add the audio and/or video tracks as required,
       Call the createOffer method to acquire an SDP offer that can be sent to the remote peer,
      
    
       Send the SDP offer and get the SDP answer from the remote peer (this exchange is not part of the WebRTC specification and can be done using any signalling layer, examples are SIP, web sockets etc),
       Once the SDP exchange has occurred the ICE checks can start in order to establish the optimal network path between the two peers. ICE candidates typically need to be passed between peers using the signalling layer,
       Once ICE has established a the DTLS handshake will occur,,
       If the DTLS handshake is successful the keying material it produces is used to initialise the SRTP contexts,
       After the SRTP contexts are initialised the RTP media and RTCP packets can be exchanged in the normal manner.
     */
    public class PeerConnectionManager : IPeerConnectionManager
    {
        private readonly IHubContext<WebRTCHub> _webRTCHub;
        private readonly ILogger<PeerConnectionManager> _logger;
        private readonly IUserManager _userManager;
        private readonly IRoomManager _roomManager;

        private readonly Mixer _mixer;
        private readonly Opusenc _opusenc = new Opusenc();
        private readonly Opusdec _opudec = new Opusdec();

        private int i = 0;
        private readonly object _lock = new { };

        private ConcurrentDictionary<string, List<RTCIceCandidate>> _candidates = new();
        private ConcurrentDictionary<string, RTCPeerConnection> _peerConnections = new();

        private static RTCConfiguration _config = new()
        {
            X_UseRtpFeedbackProfile = true,
            iceServers = new List<RTCIceServer>
            {
                new RTCIceServer
                {
                    urls = "stun:stun1.l.google.com:19302"
                },
                new RTCIceServer
                {
                    username = "webrtc",
                    credential = "webrtc",
                    credentialType = RTCIceCredentialType.password,
                    urls = "turn:turn.anyfirewall.com:443?transport=tcp"
                },
            }
        };


        private static FFOptions options = new()
        {
            UseCache = true,
            TemporaryFilesFolder = @"C:\temp",
            BinaryFolder = @"C:\ProgramData\chocolatey\lib\ffmpeg\tools\ffmpeg\bin",
        };

        public PeerConnectionManager(ILogger<PeerConnectionManager> logger, IHubContext<WebRTCHub> webRTCHub, IUserManager userManager, Mixer mixer, IRoomManager roomManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _webRTCHub = webRTCHub ?? throw new ArgumentNullException(nameof(webRTCHub));
            _peerConnections ??= new ConcurrentDictionary<string, RTCPeerConnection>();
            _userManager ??= userManager;
            _mixer ??= mixer;
            _roomManager ??= roomManager;

            Task.Run(_mixer.StartAudioProcess);
        }

        public async Task<RTCSessionDescriptionInit> CreateServerOffer(User user)
        {
            var peerConnection = new RTCPeerConnection(_config);

            var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
              //new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(new AudioFormat(AudioCodecsEnum.OPUS, 111, 48000, 2, "minptime=20;maxptime=50;useinbandfec=1;")) }, MediaStreamStatusEnum.SendRecv);
              new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.SendRecv);

            peerConnection.addTrack(audioTrack);

            peerConnection.onicegatheringstatechange += (RTCIceGatheringState obj) =>
            {
                if (peerConnection.signalingState == RTCSignalingState.have_local_offer ||
                    peerConnection.signalingState == RTCSignalingState.have_remote_offer)
                {
                    var candidates = _candidates.Where(x => x.Key == user.Id).SingleOrDefault().Value;
                    foreach (var candidate in candidates)
                    {
                        _webRTCHub.Clients.All.SendAsync("IceCandidateResult", candidate).GetAwaiter().GetResult();
                    }
                }
            };

            peerConnection.onicecandidate += (candidate) =>
            {
                if (peerConnection.signalingState == RTCSignalingState.have_local_offer ||
                    peerConnection.signalingState == RTCSignalingState.have_remote_offer)
                {
                    var candidatesList = _candidates.Where(x => x.Key == user.Id).SingleOrDefault();
                    if (candidatesList.Value is null)
                        _candidates.TryAdd(user.Id, new List<RTCIceCandidate> { candidate });
                    else
                        candidatesList.Value.Add(candidate);
                }
            };

            peerConnection.onconnectionstatechange += (state) =>
            {
                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    _peerConnections.TryRemove(user.Id, out _);
                    _mixer.RemoveUsersToRelay(user);
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    _logger.LogDebug("Peer connection connected.");
                }
            };

            var offerSdp = peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerSdp);
            _peerConnections.TryAdd(user.Id, peerConnection);
            _mixer.AddUsersToRelay(user);
            return offerSdp;
        }

        public void SetAudioRelay(User user)
        {
            user.PeerConnection.OnRtpPacketReceived += (rep, media, pkt) =>
            {
                if (media == SDPMediaTypesEnum.audio)
                {
                    try
                    {
                        //_mixer.AddRawPacket(pkt, user.MainRoom.Users.ToList(), user.Username);
                        _mixer.AddRawPacket(pkt.Payload);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e.ToString());
                    }

                    /*var usersToRelay = _roomManager.GetAll().Where(r => r.Id == user.MainRoom.Id).Select(u => u.Users).SingleOrDefault().ToList();
                    foreach (var receiverUser in usersToRelay)
                    {
                        if(user.Id == receiverUser.Id) continue;
                        receiverUser.PeerConnection?.SendRtpRaw(SDPMediaTypesEnum.audio, pkt.Payload, pkt.Header.Timestamp, pkt.Header.MarkerBit, pkt.Header.PayloadType);
                    }*/
                    /*_mixer.HasAudioData += (object e, TesteEventArgs args) =>
                    {
                        user.PeerConnection.SendRtpRaw(SDPMediaTypesEnum.audio, args.bytes,pkt.Header.Timestamp, 0, 0);
                    };*/
                }
            };
        }

        public void SetRemoteDescription(string id, RTCSessionDescriptionInit rtcSessionDescriptionInit)
        {
            if (!_peerConnections.TryGetValue(id, out var pc)) return;
            pc.setRemoteDescription(rtcSessionDescriptionInit);
        }

        public void AddIceCandidate(string id, RTCIceCandidateInit iceCandidate)
        {
            if (!_peerConnections.TryGetValue(id, out var pc)) return;
            pc.addIceCandidate(iceCandidate);
        }

        private RTPSession CreateRtpSession(List<SDPAudioVideoMediaFormat> audioFormats)
        {
            var rtpSession = new RTPSession(false, false, false, IPAddress.Loopback);
            bool hasAudio = false;

            if (audioFormats != null && audioFormats.Count > 0)
            {
                var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, audioFormats, MediaStreamStatusEnum.SendRecv);
                rtpSession.addTrack(audioTrack);
                hasAudio = true;
            }

            var sdpOffer = rtpSession.CreateOffer(null);

            // Because the SDP being written to the file is the input to ffplay the connection ports need to be changed
            // to the ones ffplay will be listening on.
            if (hasAudio)
            {
                sdpOffer.Media.Single(x => x.Media == SDPMediaTypesEnum.audio).Port = 8082;
            }

            rtpSession.Start();
            rtpSession.SetDestination(SDPMediaTypesEnum.audio, new IPEndPoint(IPAddress.Loopback, 8082), new IPEndPoint(IPAddress.Loopback, 8083));

            return rtpSession;
        }

        public RTCPeerConnection Get(string id)
        {
            var pc = _peerConnections.Where(p => p.Key == id).SingleOrDefault();
            if (pc.Value != null) return pc.Value;
            return null;
        }
    }

    public static class StreamExtension
    {
        public static byte[] ToArray(this Stream stream)
        {
            byte[] buffer = new byte[4096];
            int reader = 0;
            MemoryStream memoryStream = new MemoryStream();
            while ((reader = stream.Read(buffer, 0, buffer.Length)) != 0)
                memoryStream.Write(buffer, 0, reader);
            return memoryStream.ToArray();
        }
    }
}




//_opudec.DecodeRawAudio(pkt.Payload);
//_opusenc.EncodeRawAudioPacket(pkt.Payload);

//    //var encoder = OpusEncoder.Create(48000, 1, Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP);
//    //encoder.EnableAnalysis = true;
//    //encoder.Bitrate = (64 * 1024);
//    //encoder.Complexity = 5;
//    //encoder.PacketLossPercent = 0;
//    //encoder.UseInbandFEC = true;
//    //encoder.UseVBR = false;
//    //encoder.UseConstrainedVBR = false;

//    //var outStream = new MemoryStream();
//    //var oggin = new OpusOggWriteStream(encoder, outStream);
//    //lock(_lock)
//    //{
//    //    var audioStream = new MemoryStream(pkt.Payload, 28, pkt.Payload.Length - 28, true, true);
//    //    using var fl = File.Create(@$"C:\temp\{i}.opus");
//    //    fl.Write(audioStream.GetBuffer(), 0, (int)audioStream.Length);
//    //    fl.Close();
//    //    i++;
//    //}

//    //var outStream = new MemoryStream();
//    //var mediaAnalisys = FFProbe.Analyse(audioStream, int.MaxValue, options);
//    //audioStream.Position = 0;

//    //FFMpegArguments
//    //    .FromPipeInput(new StreamPipeSource(audioStream), options =>
//    //    {
//    //        //options.WithAudioCodec("OPUS");
//    //        //options.WithDuration(mediaAnalisys.Duration);
//    //    })
//    //    //.AddPipeInput(new StreamPipeSource(audioStream2), options =>
//    //    //{
//    //    //    options.WithDuration(durationAudio2);
//    //    //})
//    //    .OutputToPipe(new StreamPipeSink(outStream), options =>
//    //    {
//    //        options.ForceFormat("mp3");
//    //        //options.WithCustomArgument(@"-filter_complex amerge=inputs=2 -ac 2");
//    //    })
//    //    .NotifyOnOutput((str, dt) =>
//    //    {
//    //        _logger.LogInformation(str);
//    //    })
//    //    .ProcessSynchronously(true, options);

//    //using (var fileIn = new MemoryStream(pkt.Payload))
//    //using (var pcmStream = new MemoryStream())
//    //{
//    //var decoder = opusdecoder.create(48000, 1);
//    //var oggin = new opusoggreadstream(decoder, filein);

//    //    while (oggIn.HasNextPacket)
//    //    {
//    //        short[] packet = oggIn.DecodeNextPacket();
//    //        if (packet != null)
//    //        {
//    //            for (int i = 0; i < packet.Length; i++)
//    //            {
//    //                var bytes = BitConverter.GetBytes(packet[i]);
//    //                pcmStream.Write(bytes, 0, bytes.Length);
//    //            }
//    //        }
//    //    }

//    //    pcmStream.Position = 0;

//    //    var wavStream = new RawSourceWaveStream(pcmStream, new WaveFormat(48000, 1));
//    //    var sampleProvider = wavStream.ToSampleProvider();
//    //    WaveFileWriter.CreateWaveFile16(Path.Combine(Directory.GetCurrentDirectory(), "AAAAAAAAAAAAAA.wav"), sampleProvider);
//    //}
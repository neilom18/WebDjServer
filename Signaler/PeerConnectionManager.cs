using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Signaler.Hubs;
using Signaler.Models;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

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
        private ConcurrentDictionary<string, RTCPeerConnection> _peerConnections = new ConcurrentDictionary<string, RTCPeerConnection>();

        private RTCConfiguration _config = new RTCConfiguration
        {
            iceServers = new List<RTCIceServer> { new RTCIceServer { urls = Consts.STUN_SERVERS_URL } }
        };

        public PeerConnectionManager(ILogger<PeerConnectionManager> logger, IHubContext<WebRTCHub> webRTCHub)
        {
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
            _webRTCHub = webRTCHub ?? throw new System.ArgumentNullException(nameof(webRTCHub));

            _peerConnections ??= new ConcurrentDictionary<string, RTCPeerConnection>();
        }

        /// <summary>
        ///     Instantiate the RTCPeerConnection instance,
        ///     Add the audio and/or video tracks as required,
        ///     Call the createOffer method to acquire an SDP offer that can be sent to the remote peer,
        /// </summary>
        public async Task<RTCSessionDescriptionInit> CreateServerOffer(string id)
        {
            var peerConnection = new RTCPeerConnection(_config);

            // COM O OPUS NAO ESTA FUNCIONANDO AINDA
            //var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
            //    new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(new AudioFormat(AudioCodecsEnum.OPUS, 96, 48000)) }, MediaStreamStatusEnum.SendRecv);

            var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.SendRecv);

            peerConnection.addTrack(audioTrack);

            peerConnection.OnAudioFormatsNegotiated += (audioFormats) =>
            {
                _logger.LogInformation("{OnAudioFormatsNegotiated}");
            };

            peerConnection.OnTimeout += (mediaType) =>
            {
                _logger.LogInformation("{OnTimeout}");
                _logger.LogWarning($"Timeout for {mediaType}.");
            };

            peerConnection.ondatachannel += (rdc) =>
            {
                _logger.LogInformation("{ondatachannel}");
                rdc.onopen += () =>
                {
                    _logger.LogInformation($"Data channel {rdc.label} opened.");
                };

                rdc.onclose += () =>
                {
                    _logger.LogInformation($"Data channel {rdc.label} closed.");
                };

                rdc.onmessage += (datachan, type, data) =>
                {
                    _logger.LogInformation(datachan.label);
                };
            };

            peerConnection.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) =>
            {
                _logger.LogInformation("{OnStunMessageReceived}");
                bool hasUseCandidate = msg.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.UseCandidate);
                _logger.LogInformation($"STUN {msg.Header.MessageType} received from {ep}, use candidate {hasUseCandidate}.");
                _logger.LogInformation($"MSG Type {msg.Header.MessageType }");
                _logger.LogInformation($"HAS AUDIO { peerConnection.HasAudio }");
            };

            peerConnection.GetRtpChannel().OnRTPDataReceived += (arg1, arg2, data) =>
            {
                _logger.LogInformation("{OnRTPDataReceived}");
            };

            peerConnection.onicecandidateerror += (candidate, error) =>
            {
                _logger.LogInformation($"Erro ao adicionar um 'ICE Candidate remoto'. {error} {candidate}");
            };

            peerConnection.oniceconnectionstatechange += (state) =>
            {
                _logger.LogInformation($"Alterando o status da conexão do 'ICE Candidate' para {state}.");
            };

            peerConnection.onicegatheringstatechange += (RTCIceGatheringState obj) =>
            {
                _logger.LogInformation($"onicegatheringstatechange { obj }.");
            };

            peerConnection.OnSendReport += (media, sr) =>
            {
                _logger.LogInformation($"RTCP enviado para {media}\n{sr.GetDebugSummary()}");
            };

            peerConnection.OnRtcpBye += (reason) =>
            {
                _logger.LogInformation($"RTCP BYE recebido, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");
            };

            peerConnection.onicecandidate += (candidate) =>
            {
                if (peerConnection.signalingState == RTCSignalingState.have_local_offer ||
                    peerConnection.signalingState == RTCSignalingState.have_remote_offer)
                {
                    _webRTCHub.Clients.All.SendAsync("IceCandidateResult", candidate).GetAwaiter().GetResult();
                }
            };

            peerConnection.onconnectionstatechange += (state) =>
            {
                _logger.LogInformation("{onconnectionstatechange}");
                _logger.LogDebug($"Peer connection {id} state changed to {state}.");

                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    _peerConnections.TryRemove(id, out _);
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    _logger.LogDebug("Peer connection connected.");
                }
            };

            await peerConnection.createDataChannel("channel");
            var offerSdp = peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerSdp);

            _peerConnections.TryAdd(id, peerConnection);

            return offerSdp;
        }

        public void SetAudioRelay(RTCPeerConnection peerConnection, string connectionId, IList<User> usersToRelay)
        {
            peerConnection.OnRtpPacketReceived += (rep, media, pkt) =>
            {
                _logger.LogInformation("{OnRtpPacketReceived}");
                _logger.LogDebug($"RTP {media} pkt received, SSRC {pkt.Header.SyncSource}, SeqNum {pkt.Header.SequenceNumber}.");

                if (media == SDPMediaTypesEnum.audio)
                {
                    _logger.LogInformation("novo aúdio....");
                    _logger.LogDebug($"Forwarding {media} RTP packet to ffplay timestamp {pkt.Header.Timestamp}.");

                    // MANDAR O AUDIO PRA TODO MUNDO MENOS O FALANTE
                    foreach (var u in usersToRelay.Where(u => u.Id != connectionId)) u.PeerConnection?.SendAudio(pkt.Header.Timestamp, pkt.Payload);

                    // FUNCIONAA
                    //peerConnection.SendAudio(pkt.Header.Timestamp, pkt.Payload);

                    // NAO FUNCIONA
                    //rtpSession.SendRtpRaw(media, pkt.Payload, pkt.Header.Timestamp, pkt.Header.MarkerBit, pkt.Header.PayloadType);
                    //rtpSession.SendAudio(1, pkt.Payload);
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
}

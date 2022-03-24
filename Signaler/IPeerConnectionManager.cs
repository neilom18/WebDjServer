using Signaler.Models;
using SIPSorcery.Net;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Signaler
{
    public interface IPeerConnectionManager
    {
        RTCPeerConnection Get(string id);
        Task<RTCSessionDescriptionInit> CreateServerOffer(User user);
        void AddIceCandidate(string id, RTCIceCandidateInit iceCandidate);
        void SetRemoteDescription(string id, RTCSessionDescriptionInit rtcSessionDescriptionInit);
        void SetAudioRelay(User user);
    }
}
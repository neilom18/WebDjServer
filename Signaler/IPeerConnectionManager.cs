using SIPSorcery.Net;
using System.Threading.Tasks;

namespace Signaler
{
    public interface IPeerConnectionManager
    {
        RTCPeerConnection Get(string id);
        Task<RTCSessionDescriptionInit> CreateServerOffer(string id);
        void AddIceCandidate(string id, RTCIceCandidateInit iceCandidate);
        void SetRemoteDescription(string id, RTCSessionDescriptionInit rtcSessionDescriptionInit);
    }
}
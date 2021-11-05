using SIPSorcery.Net;
using System.Threading.Tasks;

namespace RTCServer
{
    public interface IPeerConnectionManager
    {
        Task<RTCSessionDescriptionInit> CreateServerOffer(string id);
        void AddIceCandidate(string id, RTCIceCandidateInit iceCandidate);
        void SetRemoteDescription(string id, RTCSessionDescriptionInit rtcSessionDescriptionInit);
    }
}
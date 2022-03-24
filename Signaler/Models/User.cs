using Newtonsoft.Json;
using SIPSorcery.Net;
using System;
using System.Collections.Generic;

namespace Signaler.Models
{
    /// <summary>
    ///     Usuário
    /// </summary>
    public class User
    {
        /// <summary>
        ///     ctor
        /// </summary>
        public User()
        {
        }

        /// <summary>
        ///     Id do usuário
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        ///     Nome do usuário
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        ///     Id da conexão do usuario
        /// </summary>
        public string ConnectionId { get; set; }

        /// <summary>
        ///     Indica se o usuario está em uma chamada
        /// </summary>
        public bool IsInCall { get; set; }

        /// <summary>
        ///     Indica a sala principal do usuário
        /// </summary>

        public Room? MainRoom { get; set; }

        // <summary>
        //      Indica as salas secundárias do usuário
        // </summary>

        public IList<Room> SideRooms { get; set; }

        /// <summary>
        ///     Peer connection vinculada ao usuario
        /// </summary>
        [JsonIgnore]
        public RTCPeerConnection? PeerConnection { get; set; }
    }
}

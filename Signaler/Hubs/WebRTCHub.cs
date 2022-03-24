using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SIPSorcery.Net;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Signaler.Hubs
{
    /// <summary>
    ///     SignalR Hub
    /// </summary>
    public class WebRTCHub : Hub
    {
        private readonly ILogger<WebRTCHub> _logger;
        private readonly IRoomManager _roomManager;
        private readonly IUserManager _userManager;
        private readonly IPeerConnectionManager _peerConnectionManager;

        /// <summary>
        ///     ctor
        /// </summary>
        public WebRTCHub(ILogger<WebRTCHub> logger, IRoomManager roomManager, IUserManager userManager, IPeerConnectionManager peerConnectionManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _roomManager = roomManager ?? throw new ArgumentNullException(nameof(roomManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _peerConnectionManager = peerConnectionManager ?? throw new ArgumentNullException(nameof(peerConnectionManager));
        }

        /// <summary>
        ///     OnConnectedAsync
        /// </summary>
        public override Task OnConnectedAsync()
        {
            _logger.LogInformation("Nova conexão criada no Hub RTC! {0}", Context.ConnectionId);

            NotifyUpdateUsers(true).GetAwaiter().GetResult();
            NotifyRoomUpdates(true).GetAwaiter().GetResult();

            return base.OnConnectedAsync();
        }

        /// <summary>
        ///     OnDisconnectedAsync
        /// </summary>
        public override Task OnDisconnectedAsync(Exception exception)
        {
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            var rooms = _roomManager.GetAll().Where(r => r.Users.Any(u => u == user));
            foreach (var room in rooms)
                room.RemoveUser(user);
            _userManager.Delete(user.Id);
            _logger.LogInformation("Conexão no Hub RTC finalizada! {0} | {1}", Context.ConnectionId, exception?.ToString() ?? string.Empty);

            NotifyUpdateUsers().GetAwaiter().GetResult();
            NotifyRoomUpdates().GetAwaiter().GetResult();

            return base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        ///     Cria uma sala
        /// </summary>
        public async Task CreateRoom(string name)
        {
            var room = _roomManager.Create(name);
            if (room is null) await Clients.Caller.SendAsync("Error", "Um erro ocorreu a criar uma sala!");
            await NotifyRoomUpdates();
        }

        /// <summary>
        ///     Seleciona a sala principal
        /// </summary>
        public void SetMainRoom(string roomId)
        {
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            var room = _roomManager.GetAll().Where(r => r.Id == roomId).Single();
            if (user.MainRoom != room)
                user.MainRoom = room;
        }

        /// <summary>
        ///     Adiciona uma sala secundária
        /// </summary>
        public void SetSideRoom(string roomId)
        {
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            var room = _roomManager.GetAll().Where(r => r.Id == roomId).Single();
            if (!user.SideRooms.Any(r => r == room))
                user.SideRooms.Add(room);
        }

        /// <summary>
        ///     Entra nas salas selecionadas
        /// </summary>
        public async Task JoinRoom()
        {
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            var rooms = _roomManager.GetAll().Where(r => r.Id == user.MainRoom?.Id || user.SideRooms.Any(u => u.Id == r.Id)).ToList();
            foreach (var room in rooms)
            {
                room.AddUser(user);
                await Groups.AddToGroupAsync(user.ConnectionId, room.Id);
                await Clients.Caller.SendAsync("JoinedRoom", room.Id);
                await Clients.Group(room.Id).SendAsync("UserJoinedRoom", user.Username);
            }
            user.IsInCall = true;
            await NotifyRoomUpdates();
            await NotifyUpdateUsers();
        }

        /// <summary>
        ///     Deixa de participar de uma sala
        /// </summary>
        public async Task LeaveRoom(string roomId)
        {
            var room = _roomManager.GetAll().Where(r => r.Id == roomId).Single();
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            room.RemoveUser(user);
            user.IsInCall = false;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, room.Id);
            //await Clients.Caller.SendAsync("ExitedRoom");
            //await Clients.Group(roomId).SendAsync("UserExitedRoom", user.Username);
            await NotifyRoomUpdates();
            await NotifyUpdateUsers();
        }

        /// <summary>
        ///     Cria um usuario
        /// </summary>
        public async Task CreateUser(string username)
        {
            var user = _userManager.Create(Context.ConnectionId, username);
            await Clients.All.SendAsync("UserCreated", JsonConvert.SerializeObject(user, JsonSerializerOptions));
            await NotifyUpdateUsers(true);
        }

        /// <summary>
        ///     Deleta um usuário
        /// </summary>
        public async Task DeleteUser()
        {
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            await Clients.All.SendAsync("UserExited", user.Username);
        }

        /// <summary>
        ///     Notifica alteraçoes nos usuarios
        /// </summary>
        public async Task NotifyUpdateUsers(bool notifyOnlyCaller = false)
        {
            var users = _userManager.GetAll();

            if (notifyOnlyCaller)
                await Clients.Caller.SendAsync("UpdateUsers", JsonConvert.SerializeObject(users, JsonSerializerOptions));

            await Clients.All.SendAsync("UpdateUsers", JsonConvert.SerializeObject(users, JsonSerializerOptions));
        }

        /// <summary>
        ///     Notifica alterações nas salas
        /// </summary>
        public async Task NotifyRoomUpdates(bool notifyOnlyCaller = false)
        {
            var rooms = _roomManager.GetAll();

            if (notifyOnlyCaller)
                await Clients.Caller.SendAsync("UpdateRooms", JsonConvert.SerializeObject(rooms, JsonSerializerOptions));

            await Clients.All.SendAsync("UpdateRooms", JsonConvert.SerializeObject(rooms, JsonSerializerOptions));
        }

        /// <summary>
        ///     Cria uma oferta SDP servidor > cliente
        /// </summary>
        public async Task<RTCSessionDescriptionInit> GetServerOffer()
        {
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            var offer = await _peerConnectionManager.CreateServerOffer(user);
            user.PeerConnection = _peerConnectionManager.Get(user.Id);
            _peerConnectionManager.SetAudioRelay(user);
            return offer;
        }

        /// <summary>
        ///     Seta a remotedescription do cliente na conexao da sala
        /// </summary>
        public void SetRemoteDescription(RTCSessionDescriptionInit rtcSessionDescriptionInit)
        {
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            //var room = _roomManager.GetAll().Where(r => r.Id == user.Room.Id).Single();
            _peerConnectionManager.SetRemoteDescription(user.Id, rtcSessionDescriptionInit);
        }

        /// <summary>
        ///     Adiciona um ice candidate
        /// </summary>
        public void AddIceCandidate(RTCIceCandidateInit iceCandidate)
        {
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            //var room = _roomManager.GetAll().Where(r => r.Id == user.Room.Id).Single();
            _peerConnectionManager.AddIceCandidate(user.Id, iceCandidate);
        }

        private JsonSerializerSettings JsonSerializerOptions =>
            new JsonSerializerSettings()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };
    }
}

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

        /// <summary>
        ///     ctor
        /// </summary>
        public WebRTCHub(ILogger<WebRTCHub> logger, IRoomManager roomManager, IUserManager userManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _roomManager = roomManager ?? throw new ArgumentNullException(nameof(roomManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }

        /// <summary>
        ///     OnConnectedAsync
        /// </summary>
        public override Task OnConnectedAsync()
        {
            _logger.LogInformation("Nova conexão criada no Hub RTC! {0}", Context.ConnectionId);
            return base.OnConnectedAsync();
        }

        /// <summary>
        ///     OnDisconnectedAsync
        /// </summary>
        public override Task OnDisconnectedAsync(Exception exception)
        {
            _userManager.Delete(Context.ConnectionId);
            _logger.LogInformation("Conexão no Hub RTC finalizada! {0} | {1}", Context.ConnectionId, exception.ToString());
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
        ///     Entra em uma sala
        /// </summary>
        public async Task JoinRoom(string roomId)
        {
            var room = _roomManager.GetAll().Where(r => r.Id == roomId).Single();
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            room.AddUser(user);
            user.Room = room;
            user.IsInCall = true;
            await Groups.AddToGroupAsync(user.ConnectionId, room.Id);
            await Clients.Caller.SendAsync("JoinedRoom", room.Id);
            await Clients.Group(roomId).SendAsync("UserJoinedRoom", user.Username);
        }

        /// <summary>
        ///     Deixa de participar de uma sala
        /// </summary>
        public async Task LeaveRoom(string roomId)
        {
            var room = _roomManager.GetAll().Where(r => r.Id == roomId).Single();
            var user = _userManager.GetAll().Where(u => u.ConnectionId == Context.ConnectionId).Single();
            room.RemoveUser(user);
            user.Room = null;
            user.IsInCall = false;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, room.Id);
            await Clients.Caller.SendAsync("ExitedRoom");
            await Clients.Group(roomId).SendAsync("UserExitedRoom", user.Username);
        }

        /// <summary>
        ///     Cria um usuario
        /// </summary>
        public async Task CreateUser(string username)
        {
            var user = _userManager.Create(Context.ConnectionId, username);
            await Clients.All.SendAsync("UserCreated", user.Username);
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
        ///     Notifica alterações nas salas
        /// </summary>
        public async Task NotifyRoomUpdates()
        {
            var rooms = _roomManager.GetAll();
            await Clients.All.SendAsync("UpdateRooms", JsonConvert.SerializeObject(rooms));
        }
    }
}

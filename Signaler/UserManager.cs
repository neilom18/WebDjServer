﻿using Signaler.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Signaler
{
    /// <summary>
    ///     Manager de usuarios do sistema
    /// </summary>
    public class UserManager : IUserManager
    {
        private ConcurrentDictionary<string, User> _users;

        /// <summary>
        ///     ctor
        /// </summary>
        public UserManager()
        {
            _users ??= new ConcurrentDictionary<string, User>();
        }

        /// <summary>
        ///     Cria um novo usuario
        /// </summary>
        public User Create(string connectionId, string username)
        {
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                Username = username,
                SideRooms = new List<Room>()
            };

            return _users.TryAdd(user.Id, user) ? user : null;
        }

        /// <summary>
        ///     Deleta um usuário
        /// </summary>
        public bool Delete(string id)
        {
            return _users.TryRemove(id, out _);
        }

        /// <summary>
        ///     Retorna todos os usuarios
        /// </summary>
        public IEnumerable<User> GetAll()
        {
            return _users.Values;
        }
    }
}

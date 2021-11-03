using System;
using System.Collections.Generic;

namespace Signaler.Models
{
    /// <summary>
    ///     Sala de chamada
    /// </summary>
    public class Room
    {
        /// <summary>
        ///     Usuários que participam desta sala
        /// </summary>
        private IList<User> _users;

        /// <summary>
        ///     ctor
        /// </summary>
        public Room()
        {
            _users ??= new List<User>();
        }

        /// <summary>
        ///     Id da sala (gerado automaticamente)
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        ///     Nome da sala
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        ///     Adiciona um usuário a sala em questão
        /// </summary>
        public void AddUser(User user)
        {
            _users.Add(user);
        }

        /// <summary>
        ///     Remove um usuário a sala em questão
        /// </summary>
        public void RemoveUser(User user)
        {
            _users.Remove(user);
        }
    }
}

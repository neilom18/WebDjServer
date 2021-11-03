using Signaler.Models;
using System.Collections.Generic;

namespace Signaler
{
    public interface IRoomManager
    {
        Room Create(string name);
        bool Delete(string id);
        IEnumerable<Room> GetAll();
    }
}
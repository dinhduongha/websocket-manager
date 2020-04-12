using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Bamboo.WebSocketManager
{
    public class WebSocketConnectionManager
    {
        private ConcurrentDictionary<string, WebSocketConnection> _sockets = new ConcurrentDictionary<string, WebSocketConnection>();
        private ConcurrentDictionary<string, List<string>> _groups = new ConcurrentDictionary<string, List<string>>();

        private ConcurrentDictionary<string, WebSocketConnection> _socketUids = new ConcurrentDictionary<string, WebSocketConnection>();
        //private ConcurrentDictionary<long, WebSocketConnection> _socketDids = new ConcurrentDictionary<long, WebSocketConnection>();

        private ILogger<WebSocketConnectionManager> _logger;
        public string ulid;
        public WebSocketConnectionManager(ILogger<WebSocketConnectionManager> logger)
        {
            _logger = logger;
            ulid = NUlid.Ulid.NewUlid().ToGuid().ToString();
        }
        public WebSocketConnection GetSocketById(string id)
        {
            return _sockets.FirstOrDefault(p => p.Key == id).Value;
        }
        public WebSocketConnection GetSocketByUId(string id)
        {
            return _socketUids.FirstOrDefault(p => p.Key == id).Value;
        }
        
        public ConcurrentDictionary<string, WebSocketConnection> GetAll()
        {
            return _sockets;
        }

        public List<string> GetAllFromGroup(string GroupID)
        {
            if (_groups.ContainsKey(GroupID))
            {
                return _groups[GroupID];
            }

            return default(List<string>);
        }

        public string GetId(WebSocketConnection socket)
        {
            return _sockets.FirstOrDefault(p => p.Value == socket).Key;
        }

        public void AddSocket(WebSocketConnection socket)
        {
            _sockets.TryAdd(socket.Id, socket);
        }
        //public void AddSocket(long did, WebSocketConnection socket)
        //{
        //    if (_sockets.ContainsKey(socket.Id) && did > 0)
        //    {
        //        socket.Did = did;
        //        _socketDids.TryAdd(did, socket);
        //    }

        //}
        //public void AddSocketUid(string uid, WebSocketConnection socket)
        //{
        //    if (_sockets.ContainsKey(socket.Id) && uid.Length > 0)
        //    {
        //        socket.Uid = uid;
        //        _socketUids.TryAdd(uid, socket);
        //    }
        //}

        public void AddToGroup(string socketID, string groupID)
        {
            if (_groups.ContainsKey(groupID))
            {
                var list = _groups[groupID];
                list.Add(socketID);
                _groups[groupID] = list;
                //_groups[groupID].Add(socketID);

                return;
            }

            _groups.TryAdd(groupID, new List<string> { socketID });
        }

        public void RemoveFromGroup(string socketID, string groupID)
        {
            if (_groups.ContainsKey(groupID))
            {
                var list = _groups[groupID];
                list.Remove(socketID);
                _groups[groupID] = list;
                //_groups[groupID].Remove(socketID);
            }
        }

        public async Task RemoveSocket(string id)
        {
            if (id == null) return;
            var skt = GetSocketById(id);
            if (skt != null)
            {
                //WebSocketConnection socket2;
                //if (skt.Uid != null)
                //{
                //    _socketUids.TryRemove(skt.Uid, out socket2);
                //}
                //if (skt.Did > 0 )
                //{
                //    _socketDids.TryRemove(skt.Did, out socket2);
                //}
            }
            WebSocketConnection socket;
            _sockets.TryRemove(id, out socket);
            try
            {
                await socket.WebSocket.CloseAsync(closeStatus: WebSocketCloseStatus.NormalClosure,
                                        statusDescription: "Closed by the WebSocketManager",
                                        cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch(Exception e)
            {
                _logger.LogError(e, "Error closing socket");
            }
        }
    }
}
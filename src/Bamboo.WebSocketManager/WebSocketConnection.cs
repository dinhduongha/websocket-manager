using System;
using System.Collections.Generic;
using System.Text;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using NUlid;

namespace Bamboo.WebSocketManager
{
    public class WebSocketConnection
    {
        public Microsoft.AspNetCore.Http.HttpContext HttpConntext { get; set; }
        public  WebSocket WebSocket { get; set; }
        private ConcurrentDictionary<string, object> _items = new ConcurrentDictionary<string, object>();

        // Connection ID
        public string Id { get; }        
        public bool IsAuthorized { get; set; }

        private long _cmdId = 0;    // Increase when send command to client

        public WebSocketConnection(Microsoft.AspNetCore.Http.HttpContext context, WebSocket webSocket)
        {
            Id = Ulid.NewUlid().ToString();
            HttpConntext = context;
            WebSocket = webSocket;            
        }

        public ConcurrentDictionary<string, object> Items()
        {
            return _items;
        }

        public void SetItem(string key, object value)
        {
            if (!_items.ContainsKey(key))
            {
                _items.TryAdd(key, value);
            }
        }

        public object GetItem(string key)
        {
            object obj = null;
            _items.TryGetValue(key, out obj);
            return obj;
        }

        public long NextCmdId()
        {
            return (++_cmdId);
        }
    }
}

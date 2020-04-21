using System;
using System.Collections.Generic;
using System.Text;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using NUlid;
using NUlid.Rng;

namespace Bamboo.WebSocketManager
{
    public class WebSocketConnection
    {
        public HttpContext HttpContext { get; set; }
        public  WebSocket WebSocket { get; set; }
        private ConcurrentDictionary<string, object> _items = new ConcurrentDictionary<string, object>();

        public Guid Guid { get; set; }
        // Connection ID
        public string Id { get; }        
        public bool IsAuthorized { get; set; }
        public DateTimeOffset ConnectedTime { get; } = DateTimeOffset.Now;
        private long _cmdId = 0;    // Increase when send command to client

        public WebSocketConnection(HttpContext context, WebSocket webSocket)
        {
            Id = Ulid.NewUlid().ToString();
            HttpContext = context;
            WebSocket = webSocket;
            ConnectedTime = DateTimeOffset.Now;
        }

        public ConcurrentDictionary<string, object> Items()
        {
            return _items;
        }

        public void SetRefGuid(Guid guid)
        {
            Guid = guid;
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

        private const long UNIXEPOCHMICROSECONDS = 62135596800000000;
        private static readonly DateTimeOffset EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly IUlidRng DEFAULTRNG = new CSUlidRng();
        public static Guid NewGuid()
        {
            var timePart = DateTimeOffset.Now;
            var randomPart = DEFAULTRNG.GetRandomBytes(10);
            var d = DateTimeOffsetToByteArray(timePart);
            byte[] bytes = new byte[] { d[3], d[2], d[1], d[0], d[5], d[4], d[7], d[6],
                                        randomPart[0], randomPart[1], randomPart[2], randomPart[3],
                                        randomPart[4], randomPart[5], randomPart[6], randomPart[7]};
            return new Guid(bytes);
            //return new byte[] { _d, _c, _b, _a, _f, _e, _h, _g, _i, _j, _k, _l, _m, _n, _o, _p };
        }
        private static byte[] DateTimeOffsetToByteArray(DateTimeOffset value)
        {
            var micros = (value.Ticks / 10) - UNIXEPOCHMICROSECONDS;
            var mb = BitConverter.GetBytes(micros / 1000);
            var mm = BitConverter.GetBytes(micros % 1000);
            return new[] { mb[5], mb[4], mb[3], mb[2], mb[1], mb[0], mm[1], mm[0] };                                  // Drop byte 6 & 7
        }
    }
}

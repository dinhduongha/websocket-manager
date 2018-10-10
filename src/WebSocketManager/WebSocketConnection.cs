using System;
using System.Collections.Generic;
using System.Text;
using System.Net.WebSockets;
using NUlid;
namespace WebSocketManager
{
    public class WebSocketConnection
    {
        public Microsoft.AspNetCore.Http.HttpContext httpConntext { get; set; }
        public  WebSocket WebSocket { get; set; }
        public string Id { get; }
        private long _cmdId = 0;
        public WebSocketConnection(Microsoft.AspNetCore.Http.HttpContext context, WebSocket webSocket)
        {
            Id = Ulid.NewUlid().ToString();
            httpConntext = context;
            WebSocket = webSocket;
        }
        public long NextCmdId()
        {
            return (++_cmdId);
        }
    }
}

using Newtonsoft.Json.Linq;
using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Bamboo.WebSocketManager;
using Bamboo.WebSocketManager.Common;
using Microsoft.Extensions.Logging;

namespace IoT
{
    public class LoginParams
    {
        public LoginParams()
        {
        }
        public string userOrEmail { get; set; }
        public string password { get; set; }
        public string accessToken { get; set; }
    }

    public class IoTAdminHandler : WebSocketHandler
    {
        public IoTAdminHandler(WebSocketConnectionManager webSocketConnectionManager, ILogger<IoTAdminHandler> logger) 
            : base(webSocketConnectionManager, new ControllerMethodInvocationStrategy(), logger)
        {
            ((ControllerMethodInvocationStrategy)MethodInvocationStrategy).Controller = this;
        }

        public override async Task OnConnected(WebSocketConnection socket)
        {
            await base.OnConnected(socket);

            var socketId = WebSocketConnectionManager.GetId(socket);

            var message = new Message()
            {
                MessageType = MessageType.Text,
                Data = $"Admin {socketId} is now connected"
            };
        }

        public override async Task OnDisconnected(WebSocketConnection socket)
        {
            var socketId = WebSocketConnectionManager.GetId(socket);

            await base.OnDisconnected(socket);

            var message = new Message()
            {
                MessageType = MessageType.Text,
                Data = $"Admin {socketId} disconnected"
            };
        }

        public async Task OnDeviceConnected(WebSocketConnection deviceSocket, ConnectParams jobConnectParams)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Admin user login
        /// </summary>
        /// <param name="socketId"></param>
        /// <param name="obj"></param>
        /// <returns></returns>

        /// Admin send "login" message to this server
        /// {"jsonrpc":"2.0","id":1,"method":"login","params":{"userOrEmail": "user@company.com", "accessToken": "accessTokenString"}}
        public object login(string socketId, JObject obj)
        {
            var socketConn = WebSocketConnectionManager.GetSocketById(socketId);
            LoginParams message;
            try
            {
                message = obj.ToObject<LoginParams>();
                if (message == null)
                {
                    return null;
                }
                /// TODO: Verify if valid user.
            }
            catch (Exception e)
            {
                var str = e.Message;
                return new RemoteException()
                {
                    Code = -1,
                    Message = "Admin login error",
                    Data = new
                    {
                        exception = e.Message
                    }
                };
            }
            // Tell admin user: your's connection was accepted
            return new { success = true, };
        }

        /// Client send "echo" message to this server, ex:
        /// {"jsonrpc":"2.0","id":2,"method":"echo","params":{"echoString": "String", "anotherInfo": "anotherInfo"}}
        public object echo(string socketId, JObject obj)
        {
            // Echo jsonrpc's params to client as jsonrpc's result.
            return obj;
        }
    }
}
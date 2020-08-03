using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

using Bamboo.WebSocketManager;
using Bamboo.WebSocketManager.Common;

namespace IoT
{
    public class Params
    {
        public Params()
        {
        }
        public long epoch { get; set; }  // Epoch time at device
        public string ulid { get; set; } // Ulid of device
    }
    public class ConnectParams : Params
    {
    }

    public class StateParams : Params
    {
        long red { get; set; } = 0;
        long green { get; set; } = 0;
        long yellow { get; set; } = 0;
    }

    public class IoTDeviceHandler : WebSocketHandler
    {
        public ILogger<IoTDeviceHandler> Logger { get; set; }
        private IoTAdminHandler _adminHandler;
        public IoTDeviceHandler(WebSocketConnectionManager webSocketConnectionManager, IoTAdminHandler adminHandler, ILogger<IoTDeviceHandler> logger)
            : base(webSocketConnectionManager, new ControllerMethodInvocationStrategy(), logger)
        {
            ((ControllerMethodInvocationStrategy)MethodInvocationStrategy).Controller = this;
            //Logger = NullLogger.Instance;
            Logger = logger;
            _adminHandler = adminHandler; // Demo DI with other websocket jsonrpc handler
        }

        public override async Task OnConnected(WebSocketConnection socket)
        {
            await base.OnConnected(socket);
            var socketId = WebSocketConnectionManager.GetId(socket);
        }

        public override async Task OnDisconnected(WebSocketConnection socket)
        {
            var socketId = WebSocketConnectionManager.GetId(socket);

            await base.OnDisconnected(socket);
        }

        /// <summary>
        /// Client send "connect" message to server at first time connected.
        /// {"jsonrpc":"2.0","id":1,"method":"connect","params":{"ulid": "", "epoch":0}}
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="jobject"></param>
        /// <returns></returns>
        public object connect(string socket, JObject jobject)
        {
            var socketConn = WebSocketConnectionManager.GetSocketById(socket);
            ConnectParams message;
            try
            {
                message = jobject.ToObject<ConnectParams>();
                if (message == null)
                {
                    return null;
                }
                /// TODO: Verify device is own device.
                _adminHandler.OnDeviceConnected(socketConn, message).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var str = e.Message;
                return new RemoteException()
                {
                    Code = -1,
                    Message = "Device connected error",
                    Data = new
                    {
                        exception = e.Message
                    }
                };
            }
            // Tell device it's connection was accept
            return new { success = true,  };
        }

        // Client repeatly send "state" message to server.
        // {"jsonrpc":"2.0","id":1,"method":"state","params":{"ulid": "", "epoch":0, "red":0, "green": 0, "yellow": 3}}
        public object state(string socket, JObject jobject)
        {
            var socketConn = WebSocketConnectionManager.GetSocketById(socket);
            /// TODO: Check if connection is valid 
            StateParams message;
            try
            {
                message = jobject.ToObject<StateParams>();
                if (message == null)
                {
                    return null;
                }
                /// TODO: Log device state to database, or forward to another service
            }
            catch (Exception e)
            {
                var str = e.Message;
                return new RemoteException()
                {
                    Code = -1,
                    Message = "Device state error",
                    Data = new
                    {
                        exception = e.Message
                    }
                };
            }
            return new { success = true, };
        }
    }
}
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text;

using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

using Bamboo.WebSocketManager.Common;

using IoT;

namespace Bamboo.Web.Controllers
{
    public class IoTMessagesController : ControllerBase
    {
        private IoTDeviceHandler _notificationsMessageHandler { get; set; }
        public IoTMessagesController(
            IoTDeviceHandler notificationsMessageHandler
            )
        {
            _notificationsMessageHandler = notificationsMessageHandler;
        }

        /// <summary>
        /// Forward command to devices
        /// </summary>
        /// <param name="id">Connection ID</param>
        /// <param name="method">Method's name</param>
        /// <param name="message">Params of jsonrpc</param>
        /// <returns></returns>
        [HttpPost]
        [Route("/iot/device/call")]
        public async Task CallRpc(string id, string method,[FromBody] string message)
        {
            if (message.Length == 0)
                message = "Unknown";
            RemoteException ex = new RemoteException(1, message);
            object[] objs = new object[1];
            objs[0] = message;
            var t = await _notificationsMessageHandler.InvokeClientMethodAsync<RemoteException>(id, method, objs);
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Forward OTA command to device over websocket jsonrpc
        /// </summary>
        /// <param name="id">Connection id</param>
        /// <returns></returns>
        [HttpPost]
        [Route("/iot/device/ota")]        
        public async Task OTAMessage(string id)
        {
            object[] objs = new object[1];
            objs[0] = new 
            { 
                host="",
                port = 0,
                url = ""
            };
            await _notificationsMessageHandler.InvokeClientMethodAsync(id, "ota", objs);
        }

        /// <summary>
        ///  Forward message to device over UDP
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        /// <param name="method"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("/iot/device/udp")]
        public async Task UdpMessage(string ip, int port, string method, string message)
        {
            object[] objs = new object[1];
            objs[0] = message;
            InvocationDescriptor invocationDescriptor = new InvocationDescriptor { MethodName = method, Params = objs[0] };
            var Data = JsonConvert.SerializeObject(invocationDescriptor);
            IPAddress addrAddress = IPAddress.Parse(ip);
            if (port <= 0) port = 33333;
            var client = new UdpClient();
            byte[] array = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(invocationDescriptor));
            await client.SendAsync(array, array.Length, new IPEndPoint(addrAddress, port));
            client = null;
        }
    }
}
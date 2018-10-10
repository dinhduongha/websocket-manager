using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MvcSample.MessageHandlers;
using WebSocketManager.Common;

namespace MvcSample.Controllers
{

    public class MessagesController : Controller
    {
        private NotificationsMessageHandler _notificationsMessageHandler { get; set; }

        public MessagesController(NotificationsMessageHandler notificationsMessageHandler)
        {
            _notificationsMessageHandler = notificationsMessageHandler;
        }

        [HttpGet]
        public async Task SendMessage(string id, [FromQueryAttribute]string message)
        {
            RemoteException ex = new RemoteException(1, "Unknown");
            await _notificationsMessageHandler.InvokeClientMethodToAllAsync("receiveMessage", ex, ex);
            await _notificationsMessageHandler.InvokeClientMethodToAllAsync("receiveMessage", message, ex);
            await _notificationsMessageHandler.InvokeClientMethodToAllAsync("receiveMessage", message);
            await _notificationsMessageHandler.InvokeClientMethodToAllAsync("receiveMessage", ex);

            await _notificationsMessageHandler.InvokeClientMethodOnlyAsync(id, "receiveMessage", message, ex);
            await _notificationsMessageHandler.SendClientResultAsync(id, "receiveResult", ex);
            await _notificationsMessageHandler.SendClientErrorAsync(id, "receiveError", ex);
            await _notificationsMessageHandler.SendClientNotifyAsync(id, "receiveNotify", ex);
            await _notificationsMessageHandler.SendAllNotifyAsync("receiveAllNotify", ex);

            //object[] objs = new object[1];
            //objs[0] = ex;
            //var t =  _notificationsMessageHandler.InvokeClientMethodAsync<RemoteException>(id, "waitMessage", objs);

        }
    }
}
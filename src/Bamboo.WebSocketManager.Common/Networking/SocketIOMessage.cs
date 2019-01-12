using System;
using Newtonsoft.Json;

namespace Bamboo.WebSocketManager.Common.Networking
{
    public enum SocketIOMessageType
    {
        /**
        Message with connection options
        */
        MessageTypeOpen = 0,
        /**
        Close connection and destroy all handle routines
        */
        MessageTypeClose = 1,
        /**
        Ping request message
        */
        MessageTypePing = 2,
        /**
        Pong response message
        */
        MessageTypePong = 3,
        /**
        Empty message
        */
        MessageTypeEmpty = 4,
        /**
        Emit request, no response
        */
        MessageTypeEmit = 5,
        /**
        Emit request, wait for response (ack)
        */
        MessageTypeAckRequest = 6,
        /**
        ack response
        */
        MessageTypeAckResponse = 7
    }
    /*
const (
open          = "0"
msg           = "4"
emptyMessage  = "40"
commonMessage = "42"
ackMessage    = "43"

CloseMessage = "1"
PingMessage = "2"
PongMessage = "3"
)
*/

    /*
    open          = "0"
    msg           = "4"
    emptyMessage  = "40"
    commonMessage = "42"
    ackMessage    = "43"

    CloseMessage = "1"
    PingMessage = "2"
    PongMessage = "3"
    */
    public class SocketIOMessage
    {
        public SocketIOMessageType Type;
        public int AckId;
        public string Method;
        public string Args;
        public string Source;
        private static string TypeToText(SocketIOMessageType msgType)
        {
            switch (msgType)
            {
                case SocketIOMessageType.MessageTypeOpen:
                    return "0";

                case SocketIOMessageType.MessageTypeClose:
                    return "1";

                case SocketIOMessageType.MessageTypePing:
                    return "2";

                case SocketIOMessageType.MessageTypePong:
                    return "3";

                case SocketIOMessageType.MessageTypeEmpty:
                    return "40";
                case SocketIOMessageType.MessageTypeEmit:
                case SocketIOMessageType.MessageTypeAckRequest:
                    return "42";
                case SocketIOMessageType.MessageTypeAckResponse:
                    return "43";
            };
            return "";
        }

        public static string Encode(SocketIOMessage msg)
        {
            var result = TypeToText(msg.Type);
            if (msg.Type == SocketIOMessageType.MessageTypeEmpty || msg.Type == SocketIOMessageType.MessageTypePing ||
                msg.Type == SocketIOMessageType.MessageTypePong)
            {
                return result;
            }

            if (msg.Type == SocketIOMessageType.MessageTypeAckRequest || msg.Type == SocketIOMessageType.MessageTypeAckResponse)
            {
                //result += strconv.Itoa(msg.AckId)
                result += msg.AckId.ToString();
            }

            if (msg.Type == SocketIOMessageType.MessageTypeOpen || msg.Type == SocketIOMessageType.MessageTypeClose)
            {
                return result + msg.Args;
            }
            if (msg.Type == SocketIOMessageType.MessageTypeAckResponse)
            {
                return result + "[" + msg.Args + "]";
            }
            string jsonMethod = JsonConvert.SerializeObject(msg.Method);
            return result + "[" + jsonMethod + "," + msg.Args + "]";
        }
    }
}

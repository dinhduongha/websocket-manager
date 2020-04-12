namespace Bamboo.WebSocketManager.Common
{
    public enum MessageType
    {
        Text,
        MethodInvocation,
        ConnectionEvent,
        MethodReturnValue,
        Binary
    }

    public class Message
    {
        public MessageType MessageType { get; set; }
        public string Data { get; set; }
        public byte[] Bytes { get; set; }
    }
}
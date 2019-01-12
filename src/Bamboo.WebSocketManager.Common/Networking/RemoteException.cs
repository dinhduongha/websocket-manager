using Newtonsoft.Json;
using System;

namespace Bamboo.WebSocketManager.Common
{
    /// <summary>
    /// An exception that occured remotely.
    /// </summary>
    public class RemoteException
    {
        /// <summary>
        /// Gets or sets the error code.
        /// </summary>
        /// <value>The error code.</value>
        [JsonProperty("code")]
        public long Code { get; set; } = -1;

        /// <summary>
        /// Gets or sets the exception message.
        /// </summary>
        /// <value>The exception message.</value>
        [JsonProperty("message")]
        public string Message { get; set; } = $"A remote exception occured";

        /// <summary>
        /// Gets or sets the exception data.
        /// </summary>
        /// <value>The exception data.</value>
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteException"/> class.
        /// </summary>
        public RemoteException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteException"/> class.
        /// </summary>
        /// <param name="exception">The exception that occured.</param>
        public RemoteException(Exception exception)
        {
            Code = -1;
            Message = $"A remote exception occured: '{exception.Message}'.";
        }

        public RemoteException(long code, string message)
        {
            Code = code;
            Message = message;
        }
        public RemoteException(long code, string message, object data)
        {
            Code = code;
            Message = message;
            Data = data;
        }

    }
}
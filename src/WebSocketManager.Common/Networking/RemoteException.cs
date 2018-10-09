using Newtonsoft.Json;
using System;

namespace WebSocketManager.Common
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
        public Int64 Code { get; set; } = -1;

        /// <summary>
        /// Gets or sets the exception message.
        /// </summary>
        /// <value>The exception message.</value>
        [JsonProperty("message")]
        public string Message { get; set; } = $"A remote exception occured";

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

        public RemoteException(Int64 code, string message)
        {
            Code = code;
            Message = message;
        }

    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace Bamboo.WebSocketManager.Common
{
    /// <summary>
    /// The base class of all method invocation strategies.
    /// </summary>
    public abstract class MethodInvocationStrategy
    {
        /// <summary>
        /// Called when an invoke method call has been received.
        /// </summary>
        /// <param name="socket">The web-socket of the client that wants to invoke a method.</param>
        /// <param name="invocationDescriptor">
        /// The invocation descriptor containing the method name and parameters.
        /// </param>
        /// <returns>Awaitable Task.</returns>
        public virtual Task<object> OnInvokeMethodReceivedAsync(string socketId, InvocationDescriptor invocationDescriptor)
        {
            throw new NotImplementedException();
        }
        public virtual Task<object> OnTextReceivedAsync(string socketId, string message)
        {
            throw new NotImplementedException();
        }
    }
}
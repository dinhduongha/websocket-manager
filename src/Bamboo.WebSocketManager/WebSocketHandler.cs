using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Bamboo.WebSocketManager.Common;
using MessagePack;
using System.Diagnostics;

namespace Bamboo.WebSocketManager
{
    public interface IWebSocketHandler
    {
    }

    public abstract class WebSocketHandler :  IWebSocketHandler, IDisposable
    {
        /*
        * KeepAlive artifacts
        */
        Timer _pingTimer;
        ConcurrentDictionary<string, DateTime> socketPongMap = new ConcurrentDictionary<string, DateTime>(2, 1);
        ConcurrentDictionary<string, DateTime> socketPingMap = new ConcurrentDictionary<string, DateTime>(2, 1);

        /// <summary>
        /// If true, will send custom "ping" messages which must be answered with a ping message
        /// Uses WebSocket.DefaultKeepAliveInterval as ping period
        /// Sockets which have not responded to 3 pings will be disconnected
        /// </summary>
        public bool SendPingMessages { get; set; }

        private async void OnPingTimer(object state)
        {
            if (SendPingMessages)
            {
                TimeSpan timeoutPeriod = TimeSpan.FromSeconds(WebSocket.DefaultKeepAliveInterval.TotalSeconds * 3);

                foreach (var item in socketPongMap)
                {
                    if (item.Value < DateTime.Now.Subtract(timeoutPeriod))
                    {
                        var socket = WebSocketConnectionManager.GetSocketById(item.Key);
                        if (socket.WebSocket.State == WebSocketState.Open)
                        {
                            await CloseSocketAsync(socket, WebSocketCloseStatus.Empty, null, CancellationToken.None);
                        }
                    }
                    else
                    {
                        if (socketPingMap[item.Key] > socketPongMap[item.Key])
                        {
                        }
                        await SendMessageAsync(item.Key, new Message() { Data = "ping", MessageType = MessageType.Text});
                        socketPingMap[item.Key] = DateTime.Now;
                    }
                }
            }
        }

        private async Task CloseSocketAsync(WebSocketConnection socket, WebSocketCloseStatus status, string message, CancellationToken token)
        {
            try
            {
                if (status == WebSocketCloseStatus.Empty)
                {
                    message = null;
                }
                await socket.WebSocket.CloseAsync(status, message, token);
            }
            catch (Exception ex)
            {
            }
            finally
            {
                await OnDisconnected(socket);
            }
        }

        protected WebSocketConnectionManager WebSocketConnectionManager { get; set; }

        private JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            TypeNameHandling = TypeNameHandling.All,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            SerializationBinder = new JsonBinderWithoutAssembly()
        };

        /// <summary>
        /// The waiting remote invocations for Server to Client method calls.
        /// </summary>

        private Dictionary<string, Dictionary<long, TaskCompletionSource<InvocationResult>>> _waitingRemoteInvocations = new Dictionary<string, Dictionary<long,TaskCompletionSource<InvocationResult>>>();

        /// <summary>
        /// Gets the method invocation strategy.
        /// </summary>
        /// <value>The method invocation strategy.</value>
        public MethodInvocationStrategy MethodInvocationStrategy { get; }
        private readonly ILogger<WebSocketHandler> _logger;
        public string ulid { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="WebSocketHandler"/> class.
        /// </summary>
        /// <param name="webSocketConnectionManager">The web socket connection manager.</param>
        /// <param name="methodInvocationStrategy">The method invocation strategy used for incoming requests.</param>
        public WebSocketHandler(WebSocketConnectionManager webSocketConnectionManager, MethodInvocationStrategy methodInvocationStrategy)
        {
            _jsonSerializerSettings.Converters.Insert(0, new PrimitiveJsonConverter());
            WebSocketConnectionManager = webSocketConnectionManager;
            MethodInvocationStrategy = methodInvocationStrategy;
            _pingTimer = new Timer(OnPingTimer, null, WebSocket.DefaultKeepAliveInterval, WebSocket.DefaultKeepAliveInterval);
            ulid = NUlid.Ulid.NewUlid().ToGuid().ToString();
        }

        /// <summary>
        /// Called when a client has connected to the server.
        /// </summary>
        /// <param name="socket">The web-socket of the client.</param>
        /// <returns>Awaitable Task.</returns>
        public virtual async Task OnConnected(WebSocketConnection socket)
        {
            WebSocketConnectionManager.AddSocket(socket);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Called when a client has disconnected from the server.
        /// </summary>
        /// <param name="socket">The web-socket of the client.</param>
        /// <returns>Awaitable Task.</returns>
        public virtual async Task OnDisconnected(WebSocketConnection socket)
        {
            await WebSocketConnectionManager.RemoveSocket(WebSocketConnectionManager.GetId(socket)).ConfigureAwait(false);
        }
        #region Pre_Post
        public virtual async Task PreRpcRequest(WebSocketConnection socket, InvocationDescriptor invocationDescriptor)
        {
            await Task.CompletedTask;
        }

        public virtual async Task PostRpcRequest(WebSocketConnection socket, InvocationDescriptor invocationDescriptor, long elapse)
        {
            await Task.CompletedTask;
        }

        public virtual async Task PreRpcResponse(WebSocketConnection socket, InvocationResult invocationResult)
        {
            await Task.CompletedTask;
        }

        public virtual async Task PostRpcResponse(WebSocketConnection socket, InvocationResult invocationResult)
        {
            await Task.CompletedTask;
        }

        public virtual async Task OnPeerResponseAsync(WebSocketConnection socket, InvocationResult invocationResult)
        {
            var socketId = WebSocketConnectionManager.GetId(socket);
            try
            {
                if (_waitingRemoteInvocations.ContainsKey(socketId) && invocationResult.Id > 0)
                {
                    if (_waitingRemoteInvocations[socketId].ContainsKey(invocationResult.Id))
                    {
                        _waitingRemoteInvocations[socketId][invocationResult.Id].SetResult(invocationResult);
                        // remove the completion source from the waiting list.
                    }
                    _waitingRemoteInvocations[socketId].Remove(invocationResult.Id);
                }
            }
            catch (Exception e)
            {
                var str = e.Message;
            }
            await Task.CompletedTask;
        }
        #endregion

        public virtual async Task OnReceivedTextAsync(WebSocketConnection socket, string serializedMessage)
        {
            var timer = new Stopwatch();
            JObject jObject = null;
            InvocationDescriptor invocationDescriptor = null;
            InvocationResult invocationResult = null;
            timer.Start();
            try
            {
                jObject = JsonConvert.DeserializeObject<JObject>(serializedMessage);                
            }
            catch (Exception e)
            {
                // ignore invalid data sent to the server.
                //socket.WebSocket.CloseOutputAsync();
                return;
            }
            try
            {
                invocationResult = jObject.ToObject<InvocationResult>();
                if ((invocationResult != null) && (invocationResult.Exception != null || invocationResult.Result != null))
                {
                    await PreRpcResponse(socket, invocationResult).ConfigureAwait(false);
                    await OnPeerResponseAsync(socket, invocationResult);
                    await PostRpcResponse(socket, invocationResult).ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception e)
            {
                // Ignore if is not jsonrpc result
            }
            try
            {
                invocationDescriptor = jObject.ToObject<InvocationDescriptor>();
            }
            catch (Exception e)
            {
                // Not jsonrpc request
                //return;
            }
            // method invocation request.
            if (invocationDescriptor != null)
            {
                await PreRpcRequest(socket, invocationDescriptor).ConfigureAwait(false);
                // retrieve the method invocation request.               
                // if the unique identifier hasn't been set then the client doesn't want a return value.
                if (invocationDescriptor.Id == 0)
                {
                    // invoke the method only.
                    try
                    {
                        var result = await MethodInvocationStrategy.OnInvokeMethodReceivedAsync(socket.Id, invocationDescriptor);
                        timer.Stop();
                        await PostRpcRequest(socket, invocationDescriptor, timer.ElapsedMilliseconds ).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        // we consume all exceptions.
                    }
                    timer.Start();
                }
                else
                {
                    try
                    {
                        var invokeResult = await MethodInvocationStrategy.OnInvokeMethodReceivedAsync(socket.Id, invocationDescriptor);

                        if (invokeResult != null)
                        {
                            string json = JsonConvert.SerializeObject(invokeResult);
                            // send a message to the client containing the result.
                            var message = new Message()
                            {
                                MessageType = MessageType.MethodReturnValue,
                                Data = json
                                //Data = JsonConvert.SerializeObject(invokeResult, _jsonSerializerSettings)
                            };
                            await SendMessageAsync(socket, message).ConfigureAwait(false);
                            timer.Stop();
                            await PostRpcRequest(socket, invocationDescriptor, timer.ElapsedMilliseconds);
                        }
                    }
                    catch (Exception e)
                    {
                    }
                }
                
            }
            else
            {
                await OnUnknownAsync(socket, jObject);
            }
        }
        
        public virtual async Task OnUnknownAsync(WebSocketConnection socket, JObject jObject)
        {
            await Task.CompletedTask;
        }

        public virtual async Task OnReceivedBinaryAsync(WebSocketConnection socket, byte[] receivedMessage)
        {
            InvocationDescriptor invocationDescriptor;
            InvocationResult invocationResult;
            var timer = new Stopwatch();
            timer.Start();
            try
            {
                invocationResult = MessagePackSerializer.Deserialize<InvocationResult>(receivedMessage);
                if ((invocationResult != null) && (invocationResult.Exception != null || invocationResult.Result != null))
                {
                    await PreRpcResponse(socket, invocationResult).ConfigureAwait(false);
                    await OnPeerResponseAsync(socket, invocationResult);
                    await PostRpcResponse(socket, invocationResult).ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception e)
            {
                // Ignore if is not msgrpc result
            }
            try
            {
                invocationDescriptor = MessagePackSerializer.Deserialize<InvocationDescriptor>(receivedMessage);
                if (invocationDescriptor == null)
                {
                    return;
                }
                await PreRpcRequest(socket, invocationDescriptor).ConfigureAwait(false);
                // retrieve the method invocation request.               
                // if the unique identifier hasn't been set then the client doesn't want a return value.
                if (invocationDescriptor.Id == 0)
                {
                    // invoke the method only.
                    try
                    {
                        var result = await MethodInvocationStrategy.OnInvokeMethodReceivedAsync(socket.Id, invocationDescriptor);
                        timer.Stop();
                        await PostRpcRequest(socket, invocationDescriptor, timer.ElapsedMilliseconds).ConfigureAwait(false);
                        
                    }
                    catch (Exception e)
                    {
                        // we consume all exceptions.
                    }
                }
                else
                {
                    try
                    {
                        var invokeResult = await MethodInvocationStrategy.OnInvokeMethodReceivedAsync(socket.Id, invocationDescriptor);
                        if (invokeResult is InvocationResult)
                            invocationResult = (InvocationResult)invokeResult;
                        if (invokeResult != null)
                        {
                            MessagePackSerializerOptions options;
                            byte[] bytes = MessagePackSerializer.Serialize(invokeResult);
                            var message = new Message()
                            {
                                MessageType = MessageType.Binary,
                                Bytes = bytes
                            };
                            await SendMessageAsync(socket, message).ConfigureAwait(false);
                            timer.Stop();
                            await PostRpcRequest(socket, invocationDescriptor, timer.ElapsedMilliseconds);
                        }
                    }
                    catch (Exception e)
                    {
                        throw;
                    }
                }
            }
            catch (Exception e)
            {
                //throw;
            }
            timer.Stop();
            await Task.CompletedTask;
        }
        #region SendMessage
        ConcurrentQueue<Tuple<WebSocket, WebSocketMessageType, byte[]>> sendQueue = new ConcurrentQueue<Tuple<WebSocket, WebSocketMessageType, byte[]>>();

        public async Task SendMessageAsync(WebSocket socket, WebSocketMessageType messageType, byte[] messageData)
        {

            if (socket.State != WebSocketState.Open)
                return;

            sendQueue.Enqueue(new Tuple<WebSocket, WebSocketMessageType, byte[]>(socket, messageType, messageData));
            await Task.Run((Action)SendMessagesInQueue);
        }

        protected void SendMessagesInQueue()
        {
            while (!sendQueue.IsEmpty)
            {
                Tuple<WebSocket, WebSocketMessageType, byte[]> item;

                if (sendQueue.TryDequeue(out item))
                {
                    try
                    {
                        item.Item1.SendAsync(buffer: new ArraySegment<byte>(array: item.Item3,
                                          offset: 0,
                                          count: item.Item3.Length),
                                          messageType: item.Item2,
                                          endOfMessage: true,
                                          cancellationToken: CancellationToken.None).Wait();

                    }
                    catch (Exception x)
                    {
                    }
                }
            }
        }

        public async Task SendMessageAsync(WebSocketConnection socket, Message message)
        {
            if (socket.WebSocket.State != WebSocketState.Open)
                return;
            bool sendAsync = false;
            var encodedMessage = (message.MessageType == MessageType.Binary)? message.Bytes: Encoding.UTF8.GetBytes(message.Data);
            try
            {
                if (sendAsync)
                {
                    await SendMessageAsync(socket.WebSocket, (message.MessageType == MessageType.Binary) ? WebSocketMessageType.Binary : WebSocketMessageType.Text, encodedMessage);
                }
                else
                {

                    await socket.WebSocket.SendAsync(buffer: new ArraySegment<byte>(array: encodedMessage,
                                                                          offset: 0,
                                                                          count: encodedMessage.Length),
                                           messageType: (message.MessageType == MessageType.Binary) ? WebSocketMessageType.Binary : WebSocketMessageType.Text,
                                           endOfMessage: true,
                                           cancellationToken: CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (WebSocketException e)
            {
                if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    await OnDisconnected(socket);
                }
            }
        }

        public async Task SendMessageAsync(string socketId, Message message)
        {
            await SendMessageAsync(WebSocketConnectionManager.GetSocketById(socketId), message).ConfigureAwait(false);
        }

        public async Task SendMessageToAllAsync(Message message)
        {
            foreach (var pair in WebSocketConnectionManager.GetAll())
            {
                try
                {
                    if (pair.Value.WebSocket.State == WebSocketState.Open)
                        await SendMessageAsync(pair.Value, message).ConfigureAwait(false);
                }
                catch (WebSocketException e)
                {
                    if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        await OnDisconnected(pair.Value);
                    }
                }
            }
        }

        public async Task SendMessageToGroupAsync(string groupID, Message message)
        {
            var sockets = WebSocketConnectionManager.GetAllFromGroup(groupID);
            if (sockets != null)
            {
                foreach (var socket in sockets)
                {
                    await SendMessageAsync(socket, message);
                }
            }
        }

        public async Task SendMessageToGroupAsync(string groupID, Message message, string except)
        {
            var sockets = WebSocketConnectionManager.GetAllFromGroup(groupID);
            if (sockets != null)
            {
                foreach (var id in sockets)
                {
                    if (id != except)
                        await SendMessageAsync(id, message);
                }
            }
        }
        #endregion
        #region RPC
        public async Task SendClientResultAsync(string socketId, long id, string methodName, object result)
        {
            // create the method invocation descriptor.
            InvocationResult invocationResult = new InvocationResult { Id = id, MethodName = methodName, Result = result };
            WebSocketConnection socket = WebSocketConnectionManager.GetSocketById(socketId);
            if (socket == null)
                return;

            var message = new Message()
            {
                MessageType = MessageType.MethodInvocation,
                Data = JsonConvert.SerializeObject(invocationResult)
            };

            await SendMessageAsync(socketId, message).ConfigureAwait(false);
        }

        public async Task SendClientErrorAsync(string socketId, long id, string methodName, RemoteException error)
        {
            // create the method invocation descriptor.
            InvocationResult invocationResult = new InvocationResult {Id = id, MethodName = methodName, Exception = error };
            WebSocketConnection socket = WebSocketConnectionManager.GetSocketById(socketId);
            if (socket == null)
                return;

            var message = new Message()
            {
                MessageType = MessageType.MethodInvocation,
                Data = JsonConvert.SerializeObject(invocationResult)
            };

            await SendMessageAsync(socketId, message).ConfigureAwait(false);
        }

        public async Task SendClientNotifyAsync(string socketId, string methodName, object result)
        {
            // create the method invocation descriptor.
            InvocationDescriptor invocationDescriptor = new InvocationDescriptor { Id = 0, MethodName = methodName, Params = result };
            WebSocketConnection socket = WebSocketConnectionManager.GetSocketById(socketId);
            if (socket == null)
                return;

            var message = new Message()
            {
                MessageType = MessageType.MethodInvocation,
                Data = JsonConvert.SerializeObject(invocationDescriptor)
            };

            await SendMessageAsync(socketId, message).ConfigureAwait(false);
        }

        public async Task SendAllNotifyAsync(string methodName, object result)
        {
            // create the method invocation descriptor.
            InvocationDescriptor invocationDescriptor = new InvocationDescriptor { Id = 0, MethodName = methodName, Params = result };

            var message = new Message()
            {
                MessageType = MessageType.MethodInvocation,
                Data = JsonConvert.SerializeObject(invocationDescriptor)
            };

            foreach (var pair in WebSocketConnectionManager.GetAll())
            {
                try
                {
                    if (pair.Value.WebSocket.State == WebSocketState.Open)
                        await SendMessageAsync(pair.Value, message).ConfigureAwait(false);
                }
                catch (WebSocketException e)
                {
                    if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        await OnDisconnected(pair.Value);
                    }
                }
            }
        }

        public async Task SendGroupNotifyAsync(string groupID, string methodName, object result)
        {
            // create the method invocation descriptor.
            InvocationDescriptor invocationDescriptor = new InvocationDescriptor { Id = 0, MethodName = methodName, Params = result };

            var message = new Message()
            {
                MessageType = MessageType.MethodInvocation,
                Data = JsonConvert.SerializeObject(invocationDescriptor)
            };

            var sockets = WebSocketConnectionManager.GetAllFromGroup(groupID);
            if (sockets != null)
            {
                foreach (var id in sockets)
                {
                    await SendMessageAsync(id, message);
                }
            }
        }
        public async Task InvokeClientMethodAsync(string socketId, string methodName, object[] arguments)
        {
            object methodParams = null;
            if (arguments.Length == 1)
            {
                methodParams = arguments[0];
            }
            else
            {
                methodParams = arguments;
            }
            // create the method invocation descriptor.
            WebSocketConnection socket = WebSocketConnectionManager.GetSocketById(socketId);
            InvocationDescriptor invocationDescriptor = new InvocationDescriptor { MethodName = methodName, Params = methodParams };
            if (socket == null)
                return;

            invocationDescriptor.Id = socket.NextCmdId();
            var message = new Message()
            {
                MessageType = MessageType.MethodInvocation,
                Data = JsonConvert.SerializeObject(invocationDescriptor)
            };

            await SendMessageAsync(socketId, message).ConfigureAwait(false);
        }

        public async Task<T> InvokeClientMethodAsync<T>(string socketId, string methodName, object[] arguments)
        {
            // create the method invocation descriptor.
            object methodParams = null;
            if (arguments.Length == 1)
            {
                methodParams = arguments[0];
            }
            else
            {
                methodParams = arguments;
            }
            InvocationDescriptor invocationDescriptor = new InvocationDescriptor { MethodName = methodName, Params = methodParams };
            WebSocketConnection socket = WebSocketConnectionManager.GetSocketById(socketId);
            // generate a unique identifier for this invocation.
            if (socket == null)
                return default(T);
            invocationDescriptor.Id = socket.NextCmdId(); // Guid.NewGuid();

            // add ourselves to the waiting list for return values.
            TaskCompletionSource<InvocationResult> task = new TaskCompletionSource<InvocationResult>();
            // after a timeout of 60 seconds we will cancel the task and remove it from the waiting list.
            new CancellationTokenSource(1000 * 60).Token.Register(() => { _waitingRemoteInvocations[socketId].Remove(invocationDescriptor.Id); task.TrySetCanceled(); });
            if (!_waitingRemoteInvocations.ContainsKey(socketId))
            {
                _waitingRemoteInvocations[socketId] = new Dictionary<long, TaskCompletionSource<InvocationResult>>();
            }
            _waitingRemoteInvocations[socketId].Add(invocationDescriptor.Id, task);

            // send the method invocation to the client.
            var message = new Message() {
                MessageType = MessageType.MethodInvocation,
                //Data = JsonConvert.SerializeObject(invocationDescriptor, _jsonSerializerSettings)
                Data = JsonConvert.SerializeObject(invocationDescriptor)
            };
            await SendMessageAsync(socketId, message).ConfigureAwait(false);

            // wait for the return value elsewhere in the program.
            InvocationResult result = await task.Task;

            // ... we just got an answer.

            // if we have completed successfully:
            if (task.Task.IsCompleted)
            {
                // there was a remote exception so we throw it here.
                if (result.Exception != null)
                    throw new Exception(result.Exception.Message);

                // return the value.

                // support null.
                if (result.Result == null) return default(T);
                // cast anything to T and hope it works.
                return (T)result.Result;
            }

            // if we reach here we got cancelled or alike so throw a timeout exception.
            throw new TimeoutException(); // todo: insert fancy message here.
        }
        #endregion
        #region HighLevel_Functions
        public async Task InvokeClientMethodToAllAsync(string methodName, object[] arguments)
        {
            foreach (var pair in WebSocketConnectionManager.GetAll())
            {
                try
                {
                    if (pair.Value.WebSocket.State == WebSocketState.Open)
                        await InvokeClientMethodAsync(pair.Key, methodName, arguments).ConfigureAwait(false);
                }
                catch (WebSocketException e)
                {
                    if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        await OnDisconnected(pair.Value);
                    }
                }
            }
        }

        public async Task InvokeClientMethodToGroupAsync(string groupID, string methodName, params object[] arguments)
        {
            var sockets = WebSocketConnectionManager.GetAllFromGroup(groupID);
            if (sockets != null)
            {
                foreach (var id in sockets)
                {
                    await InvokeClientMethodAsync(id, methodName, arguments);
                }
            }
        }

        public async Task InvokeClientMethodToGroupAsync(string groupID, string methodName, string except, params object[] arguments)
        {
            var sockets = WebSocketConnectionManager.GetAllFromGroup(groupID);
            if (sockets != null)
            {
                foreach (var id in sockets)
                {
                    if (id != except)
                        await InvokeClientMethodAsync(id, methodName, arguments);
                }
            }
        }

        public async Task InvokeClientMethodOnlyAsync(string socketId, string method) => await InvokeClientMethodAsync(socketId, method, new object[] { });

        public async Task InvokeClientMethodOnlyAsync<T1>(string socketId, string method, T1 arg1) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2>(string socketId, string method, T1 arg1, T2 arg2) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16 });

        public async Task<Result> InvokeClientMethodAsync<Result>(string socketId, string method) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { });

        public async Task<Result> InvokeClientMethodAsync<Result, T1>(string socketId, string method, T1 arg1) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2>(string socketId, string method, T1 arg1, T2 arg2) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16 });

        public async Task InvokeClientMethodToAllAsync(string method) => await InvokeClientMethodToAllAsync(method, new object[] { });

        public async Task InvokeClientMethodToAllAsync<T1>(string method, T1 arg1) => await InvokeClientMethodToAllAsync(method, new object[] { arg1 });

        public async Task InvokeClientMethodToAllAsync<T1, T2>(string method, T1 arg1, T2 arg2) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3>(string method, T1 arg1, T2 arg2, T3 arg3) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16 });
        #endregion
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _pingTimer.Dispose();
        }
    }
}
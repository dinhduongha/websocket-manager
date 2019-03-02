using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Bamboo.WebSocketManager.Common;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

namespace Bamboo.WebSocketManager
{
    /// <summary>
    /// Type that specify happenend reconnection
    /// </summary>
    public enum ReconnectionType
    {
        /// <summary>
        /// Type used for initial connection to websocket stream
        /// </summary>
        Initial = 0,

        /// <summary>
        /// Type used when connection to websocket was lost in meantime
        /// </summary>
        Lost = 1,

        /// <summary>
        /// Type used when connection to websocket was lost by not receiving any message in given timerange
        /// </summary>
        NoMessageReceived = 2,

        /// <summary>
        /// Type used after unsuccessful previous reconnection
        /// </summary>
        Error = 3,

        /// <summary>
        /// Type used when reconnection was requested by user
        /// </summary>
        ByUser = 4
    }

    /// <summary>
    /// Type that specify happenend disconnection
    /// </summary>
    public enum DisconnectionType
    {
        /// <summary>
        /// Type used for exit event, disposing of the websocket client
        /// </summary>
        Exit = 0,

        /// <summary>
        /// Type used when connection to websocket was lost in meantime
        /// </summary>
        Lost = 1,

        /// <summary>
        /// Type used when connection to websocket was lost by not receiving any message in given timerange
        /// </summary>
        NoMessageReceived = 2,

        /// <summary>
        /// Type used when connection or reconnection returned error
        /// </summary>
        Error = 3,

        /// <summary>
        /// Type used when disconnection was requested by user
        /// </summary>
        ByUser = 4
    }

    public class CloseEventArgs: EventArgs
    {
    }

    public class TextMessageEventArgs : EventArgs
    {
        public string Message { get;  }
        public TextMessageEventArgs(string message)
        {
            Message = message;
        }
    }

    public class BinaryMessageEventArgs : EventArgs
    {
    }


    public class Connection
    {
        public string ConnectionId { get; set; }
        private string _url;
        private string _token;
        private long _cmdId = 0;    // Inc when send command to server
        private Timer _lastChanceTimer;
        private DateTime _lastReceivedMsg = DateTime.UtcNow;
        //private readonly Func<ClientWebSocket> _clientFactory;        

        private bool _disposing = false;
        private CancellationTokenSource _cancelation;
        private CancellationTokenSource _cancelationTotal;

        private ClientWebSocket _clientWebSocket { get; set; }

        #region Public Events

        /// <summary>
        /// Occurs when the WebSocket connection has been established.
        /// </summary>
        public event EventHandler OnConnect;

        /// <summary>
        /// Occurs when the WebSocket connection has been closed.
        /// </summary>
        public event EventHandler<CloseEventArgs> OnClose;

        /// <summary>
        /// Occurs when the <see cref="WebSocket"/> gets an error.
        /// </summary>
        public event EventHandler<System.IO.ErrorEventArgs> OnError;

        /// <summary>
        /// Occurs when the <see cref="WebSocket"/> receives a message.
        /// </summary>
        public event EventHandler<TextMessageEventArgs> OnTextMessage;

        /// <summary>
        /// Occurs when the <see cref="WebSocket"/> receives a message.
        /// </summary>
        public event EventHandler<BinaryMessageEventArgs> OnBinaryMessage;

        #endregion


        private JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            TypeNameHandling = TypeNameHandling.All,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            SerializationBinder = new JsonBinderWithoutAssembly()
        };

        private readonly BlockingCollection<Message> _messagesToSendQueue = new BlockingCollection<Message>();

        /// <summary>
        /// Gets the method invocation strategy.
        /// </summary>
        /// <value>The method invocation strategy.</value>
        public MethodInvocationStrategy MethodInvocationStrategy { get; }

        /// <summary>
        /// The waiting remote invocations for Client to Server method calls.
        /// </summary>
        private Dictionary<long, TaskCompletionSource<InvocationResult>> _waitingRemoteInvocations = new Dictionary<long, TaskCompletionSource<InvocationResult>>();

        /// <summary>
        /// Time range in ms, how long to wait before reconnecting if no message comes from server.
        /// Default 60000 ms (1 minute)
        /// </summary>
        public int ReconnectTimeoutMs { get; set; } = 60 * 1000;

        /// <summary>
        /// Time range in ms, how long to wait before reconnecting if last reconnection failed.
        /// Default 60000 ms (1 minute)
        /// </summary>
        public int ErrorReconnectTimeoutMs { get; set; } = 10 * 1000;

        /// <summary>
        /// Returns true if Start() method was called at least once. False if not started or disposed
        /// </summary>
        public bool IsStarted { get; private set; }

        /// <summary>
        /// Returns true if client is running and connected to the server
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Terminate the websocket connection and cleanup everything
        /// </summary>
        public void Dispose()
        {            
            _disposing = true;            
            try
            {
                _lastChanceTimer?.Dispose();
                _cancelation?.Cancel();
                _cancelationTotal?.Cancel();
                _clientWebSocket?.Abort();
                _clientWebSocket?.Dispose();
                _cancelation?.Dispose();
                _cancelationTotal?.Dispose();
                _messagesToSendQueue?.Dispose();
            }
            catch (Exception e)
            {                
            }
            IsStarted = false;
            _clientWebSocket = null;
            //_disconnectedSubject.OnNext(DisconnectionType.Exit);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Connection"/> class.
        /// </summary>
        /// <param name="methodInvocationStrategy">The method invocation strategy used for incoming requests.</param>
        public Connection(MethodInvocationStrategy methodInvocationStrategy)
        {
            MethodInvocationStrategy = methodInvocationStrategy;
            _jsonSerializerSettings.Converters.Insert(0, new PrimitiveJsonConverter());
        }

        public async Task<bool> StartConnectionAsync(string uri, string token = "")
        {   
            if (IsStarted)
            {
                return IsStarted;
            }            
            _cancelation = new CancellationTokenSource();
            _cancelationTotal = new CancellationTokenSource();
            _url = uri;
            _token = token;
            IsStarted = true;
            await StartClient(new Uri(_url), _cancelation.Token, ReconnectionType.Initial).ConfigureAwait(false);
            StartBackgroundThreadForSending();
            
            await Task.CompletedTask;
            return IsStarted;
        }

        private async Task StartClient(Uri uri, CancellationToken token, ReconnectionType type)
        {
            // also check if connection was lost, that's probably why we get called multiple times.
            if (_clientWebSocket == null || _clientWebSocket.State != WebSocketState.Open)
            {
                // create a new web-socket so the next connect call works.
                _clientWebSocket?.Dispose();
                //var options = new ClientWebSocketOptions();
                _clientWebSocket = new ClientWebSocket();
                _clientWebSocket.Options.KeepAliveInterval = new TimeSpan(0, 0, 15, 0);
            
                if (!string.IsNullOrEmpty(_token))
                {
                    _clientWebSocket.Options.SetRequestHeader("Authorization", "Bearer " + _token);
                }
            }
            // don't do anything, we are already connected.
            else return;            
            try
            {
                //await _clientWebSocket.ConnectAsync(uri, CancellationToken.None).ConfigureAwait(false);
                await _clientWebSocket.ConnectAsync(uri, token).ConfigureAwait(false);
                OnConnect?.Invoke(this, EventArgs.Empty);
                IsRunning = true;
                await Receive(_clientWebSocket, token, async (receivedMessage) =>
                {
                    JObject jObject = null;
                    InvocationDescriptor invocationDescriptor = null;                    
                    try
                    {
                        jObject = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(receivedMessage);
                        //invocationDescriptor = JsonConvert.DeserializeObject<InvocationDescriptor>(serializedMessage);
                        invocationDescriptor = jObject.ToObject<InvocationDescriptor>();
                        //if (invocationDescriptor == null) return;
                    }
                    catch (Exception ex)
                    {
                        // ignore invalid data sent to the server.                        
                    }
                    if (jObject == null)
                    {
                        try
                        {
                            var obj = MethodInvocationStrategy.OnTextReceivedAsync("", receivedMessage);
                        }
                        catch (Exception ex)
                        {

                        }
                        return;
                    }
                    if (invocationDescriptor != null && invocationDescriptor.Params != null)
                    {
                        if (invocationDescriptor.Id == 0)
                        {
                            // invoke the method only.
                            try
                            {
                                await MethodInvocationStrategy.OnInvokeMethodReceivedAsync("", invocationDescriptor);
                            }
                            catch (Exception)
                            {
                            // we consume all exceptions.                            
                            return;
                            }
                        }
                        else
                        {
                            // invoke the method and get the result.
                            InvocationResult invokeResult;
                            try
                            {
                                // create an invocation result with the results.
                                invokeResult = new InvocationResult()
                                {
                                    Id = invocationDescriptor.Id,
                                    Result = await MethodInvocationStrategy.OnInvokeMethodReceivedAsync("", invocationDescriptor),
                                    Exception = null
                                };
                            }
                        // send the exception as the invocation result if there was one.
                        catch (Exception ex)
                            {
                                invokeResult = new InvocationResult()
                                {
                                    Id = invocationDescriptor.Id,
                                    Result = null,
                                    Exception = new RemoteException(ex)
                                };
                            }

                            // send a message to the server containing the result.
                            var message = new Message()
                            {
                                MessageType = MessageType.MethodReturnValue,
                                Data = JsonConvert.SerializeObject(invokeResult)
                            };
                            //var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, _jsonSerializerSettings));
                            //var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message.Data, _jsonSerializerSettings));
                            //await _clientWebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
                            await Send(message.Data);
                        }
                    }
                    else
                    {
                        try
                        {
                            var invocationResult = jObject.ToObject<InvocationResult>();
                            if ((invocationResult != null) && (invocationResult.Exception != null || invocationResult.Result != null))
                            {
                            // find the completion source in the waiting list.
                            if (_waitingRemoteInvocations.ContainsKey(invocationResult.Id))
                                {
                                // set the result of the completion source so the invoke method continues executing.
                                _waitingRemoteInvocations[invocationResult.Id].SetResult(invocationResult);
                                // remove the completion source from the waiting list.
                                _waitingRemoteInvocations.Remove(invocationResult.Id);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            var str = e.Message;
                        }
                    }
                });
                ActivateLastChance();
            }
            catch(Exception e)
            {
                Console.WriteLine($"{DateTime.Now} _clientWebSocket.ConnectAsync Exception: {e.Message}");
                IsRunning = false;
                await Task.Delay(ErrorReconnectTimeoutMs, _cancelation.Token).ConfigureAwait(false);
                await Reconnect(ReconnectionType.Error).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Force reconnection. 
        /// Closes current websocket stream and perform a new connection to the server.
        /// </summary>
        public async Task Reconnect()
        {
            if (!IsStarted)
            {                
                return;
            }
            await Reconnect(ReconnectionType.ByUser).ConfigureAwait(false);
        }

        /// <summary>
        /// Send message to the websocket channel. 
        /// It inserts the message to the queue and actual sending is done on an other thread
        /// </summary>
        /// <param name="message">Message to be sent</param>
        public Task Send(string message)
        {
            var msg = new Message()
            {
                MessageType = MessageType.MethodReturnValue,
                Data = message
                //Data = JsonConvert.SerializeObject(invokeResult, _jsonSerializerSettings)
            };
            _messagesToSendQueue.Add(msg);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Send message to the websocket channel. 
        /// It doesn't use a sending queue, 
        /// beware of issue while sending two messages in the exact same time 
        /// on the full .NET Framework platform
        /// </summary>
        /// <param name="message">Message to be sent</param>
        public Task SendInstant(string message)
        {
            var msg = new Message()
            {
                MessageType = MessageType.MethodReturnValue,
                Data = message
                //Data = JsonConvert.SerializeObject(invokeResult, _jsonSerializerSettings)
            };
            return SendInternal(msg);
        }

        private async Task Reconnect(ReconnectionType type)
        {
            IsRunning = false;
            if (_disposing)
                return;
            if (type != ReconnectionType.Error)
            {
                //_disconnectedSubject.OnNext(TranslateTypeToDisconnection(type));
            }
            
            _cancelation.Cancel();
            await Task.Delay(1000).ConfigureAwait(false);            
            _cancelation = new CancellationTokenSource();
            await StartClient(new Uri(_url), _cancelation.Token, type).ConfigureAwait(false);
        }

        private async Task SendInternal(Message message)
        {            
            var buffer = Encoding.UTF8.GetBytes(message.Data);
            var messageSegment = new ArraySegment<byte>(buffer);
            if (_clientWebSocket != null)
            { 
                await _clientWebSocket.SendAsync(messageSegment, WebSocketMessageType.Text, true, _cancelation.Token).ConfigureAwait(false);
            }
        }

        private async Task SendFromQueue()
        {
            try
            {
                foreach (var message in _messagesToSendQueue.GetConsumingEnumerable(_cancelationTotal.Token))
                {
                    try
                    {
                        await SendInternal(message).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        await Task.Delay(1000, _cancelationTotal.Token);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // task was canceled, ignore
            }
            catch (Exception e)
            {
                if (_cancelationTotal.IsCancellationRequested || _disposing)
                {
                    // disposing/canceling, do nothing and exit
                    return;
                }
                StartBackgroundThreadForSending();
            }
        }
        private void StartBackgroundThreadForSending()
        {
#pragma warning disable 4014
            Task.Factory.StartNew(_ => SendFromQueue(), TaskCreationOptions.LongRunning, _cancelationTotal.Token);
#pragma warning restore 4014
        }

        private void ActivateLastChance()
        {
            var timerMs = 1000 * 5;
            _lastChanceTimer = new Timer(LastChance, null, timerMs, timerMs);
        }

        private void DeactiveLastChance()
        {
            _lastChanceTimer?.Dispose();
            _lastChanceTimer = null;
        }

        private void LastChance(object state)
        {
            var timeoutMs = Math.Abs(ReconnectTimeoutMs);
            var diffMs = Math.Abs(DateTime.UtcNow.Subtract(_lastReceivedMsg).TotalMilliseconds);
            if (diffMs > timeoutMs)
            {

                DeactiveLastChance();
                _clientWebSocket?.Abort();
                _clientWebSocket?.Dispose();
#pragma warning disable 4014
                Reconnect(ReconnectionType.NoMessageReceived);
#pragma warning restore 4014
            }
        }

        /// <summary>
        /// Send a method invoke request to the server and waits for a reply.
        /// </summary>
        /// <param name="invocationDescriptor">Example usage: set the MethodName to SendMessage and set the arguments to the connectionID with a text message</param>
        /// <returns>An awaitable task with the return value on success.</returns>
        public async Task<T> SendAsync<T>(InvocationDescriptor invocationDescriptor)
        {
            // generate a unique identifier for this invocation.
            invocationDescriptor.Id = (_cmdId++);

            // add ourselves to the waiting list for return values.
            TaskCompletionSource<InvocationResult> task = new TaskCompletionSource<InvocationResult>();
            // after a timeout of 60 seconds we will cancel the task and remove it from the waiting list.
            new CancellationTokenSource(1000 * 60).Token.Register(() => { _waitingRemoteInvocations.Remove(invocationDescriptor.Id); task.TrySetCanceled(); });
            _waitingRemoteInvocations.Add(invocationDescriptor.Id, task);

            // send the method invocation to the server.
            //var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Message { MessageType = MessageType.MethodInvocation, Data = JsonConvert.SerializeObject(invocationDescriptor, _jsonSerializerSettings) }, _jsonSerializerSettings));
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(invocationDescriptor/*, _jsonSerializerSettings*/));
            await _clientWebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

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

        /// <summary>
        /// Send a method invoke request to the server.
        /// </summary>
        /// <param name="invocationDescriptor">Example usage: set the MethodName to SendMessage and set the arguments to the connectionID with a text message</param>
        /// <returns>An awaitable task.</returns>
        public async Task SendAsync(InvocationDescriptor invocationDescriptor)
        {
            invocationDescriptor.Id = (_cmdId++);
            // send the method invocation to the server.
            //var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Message { MessageType = MessageType.MethodInvocation, Data = JsonConvert.SerializeObject(invocationDescriptor) }));
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(invocationDescriptor/*, _jsonSerializerSettings*/));
            var str = JsonConvert.SerializeObject(invocationDescriptor/*, _jsonSerializerSettings*/);
            await _clientWebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task StopConnectionAsync()
        {
            if (_clientWebSocket != null)
            {
                await _clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task Receive(ClientWebSocket clientWebSocket, CancellationToken token, Action<string> handleMessage)
        {
            while (_clientWebSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                ArraySegment<Byte> buffer = new ArraySegment<byte>(new Byte[1024 * 4]);                
                WebSocketReceiveResult result = null;
                using (var ms = new MemoryStream())
                {
                    do
                    {
                        result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                        ms.Write(buffer.Array, buffer.Offset, result.Count);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                    }
                    while (!result.EndOfMessage);
                    _lastReceivedMsg = DateTime.UtcNow;
                    ms.Seek(0, SeekOrigin.Begin);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        try
                        {
                            string serializedMessage = null;
                            using (var reader = new StreamReader(ms, Encoding.UTF8))
                            {
                                serializedMessage = await reader.ReadToEndAsync().ConfigureAwait(false);
                            }
                            OnTextMessage?.Invoke(this, new TextMessageEventArgs(serializedMessage));
                            //handleMessage(serializedMessage);
                        }
                        catch (Exception e)
                        {
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        OnBinaryMessage?.Invoke(this, new BinaryMessageEventArgs());
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
                        _clientWebSocket = null;
                        _cancelation.Cancel();
                        OnClose?.Invoke(this, new CloseEventArgs());
                        break;
                    }
                }                
            }            
        }
        
        public async Task SendOnlyAsync(string method) => await SendAsync(new InvocationDescriptor { MethodName = method, Params = new object[] { } });
        
        public async Task SendOnlyAsync<T1>(string method, T1 arg1) => await SendAsync(new InvocationDescriptor { MethodName = method, Params = new object[] { arg1 } });
        /*
        public async Task SendOnlyAsync<T1, T2>(string method, T1 arg1, T2 arg2) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2 } });

        public async Task SendOnlyAsync<T1, T2, T3>(string method, T1 arg1, T2 arg2, T3 arg3) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6, T7>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15 } });

        public async Task SendOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16) => await SendAsync(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16 } });
        */
        
        public async Task<Result> SendAsync<Result>(string method) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Params = new object[] { } });

        public async Task<Result> SendAsync<Result, T1>(string method, T1 arg1) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Params = new object[] { arg1 } });
        /*
        public async Task<Result> SendAsync<Result, T1, T2>(string method, T1 arg1, T2 arg2) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3>(string method, T1 arg1, T2 arg2, T3 arg3) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6, T7>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15 } });

        public async Task<Result> SendAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16) => await SendAsync<Result>(new InvocationDescriptor { MethodName = method, Arguments = new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16 } });
        */
    }

 
}
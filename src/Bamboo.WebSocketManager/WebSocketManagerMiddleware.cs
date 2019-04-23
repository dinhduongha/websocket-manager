﻿using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Bamboo.WebSocketManager.Common;

namespace Bamboo.WebSocketManager
{
    public class WebSocketManagerMiddleware
    {
        private readonly RequestDelegate _next;
        private WebSocketHandler _webSocketHandler { get; set; }

        private JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            TypeNameHandling = TypeNameHandling.All,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            SerializationBinder = new JsonBinderWithoutAssembly()
        };

        public WebSocketManagerMiddleware(RequestDelegate next,
                                          WebSocketHandler webSocketHandler)
        {
            _jsonSerializerSettings.Converters.Insert(0, new PrimitiveJsonConverter());
            _next = next;
            _webSocketHandler = webSocketHandler;
        }

        public async Task Invoke(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                if (context.Request.PathBase.StartsWithSegments("/socket.io"))
                {
                }
                await _next.Invoke(context);
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            var webSocketConnection = new WebSocketConnection(context, socket);
            //await _webSocketHandler.OnConnected(webSocketConnection);
            await _webSocketHandler.OnConnected(webSocketConnection).ConfigureAwait(false);

            await Receive(webSocketConnection, async (result, serializedMessage) =>
            {
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    //Message message = JsonConvert.DeserializeObject<Message>(serializedMessage, _jsonSerializerSettings);                    
                    await _webSocketHandler.OnReceivedTextAsync(webSocketConnection, serializedMessage).ConfigureAwait(false);
                    //await _webSocketHandler.OnReceivedTextAsync(webSocketConnection, serializedMessage);
                    return;
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    try
                    {
                        await _webSocketHandler.OnReceivedBinaryAsync(webSocketConnection, serializedMessage);
                    }
                    catch (WebSocketException)
                    {
                        throw; //let's not swallow any exception for now
                    }
                    return;
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    try
                    {
                        await _webSocketHandler.OnDisconnected(webSocketConnection);
                    }
                    catch (WebSocketException)
                    {
                        throw; //let's not swallow any exception for now
                    }
                    return;
                }
            });
        }

        private async Task Receive(WebSocketConnection socket, Action<WebSocketReceiveResult, string> handleMessage)
        {
            while (socket.WebSocket.State == WebSocketState.Open)
            {
                ArraySegment<Byte> buffer = new ArraySegment<byte>(new Byte[1024 * 4]);
                string message = null;
                WebSocketReceiveResult result = null;
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        do
                        {
                            result = await socket.WebSocket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                            ms.Write(buffer.Array, buffer.Offset, result.Count);
                        }
                        while (!result.EndOfMessage);

                        ms.Seek(0, SeekOrigin.Begin);

                        using (var reader = new StreamReader(ms, Encoding.UTF8))
                        {
                            message = await reader.ReadToEndAsync().ConfigureAwait(false);
                        }
                    }

                    handleMessage(result, message);
                }
                catch (WebSocketException e)
                {
                    if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        socket.WebSocket.Abort();
                    }
                }
            }

            await _webSocketHandler.OnDisconnected(socket);
        }
    }
}
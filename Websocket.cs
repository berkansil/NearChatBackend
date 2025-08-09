using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NearChatBackend
{

    class ConnectionManager(Database db)
    {
        private readonly Database _db = db;
        private ConcurrentDictionary<int, List<UserHandler>> openConnections = new();
        private ConcurrentDictionary<int, UserHandler> latestSession = new(); //This list stores the latest session from a user to handle status.

        public void RegisterSession(UserHandler session, int channelId)
        {
            List<UserHandler> connections = openConnections.GetOrAdd(channelId, (_) => []); //This creates a new list if it doesn't exist already

            connections.Add(session);
            latestSession[channelId] = session;

            _db.UpdateStatus(session.userId, Status.Online);
        }

        public void RemoveSession(UserHandler session, int channelId)
        {
            if (latestSession.TryGetValue(session.userId, out UserHandler? lastSession))
            {
                if (lastSession == session)
                {
                    _db.UpdateStatus(session.userId, Status.Offline);
                }
            }

            if (openConnections.TryGetValue(channelId, out List<UserHandler>? connections))
            {
                connections.Remove(session);
                if (connections.Count == 0)
                {
                    openConnections.TryRemove(channelId, out _);
                }
            }
        }

        public async void SendMessage(Message message, int channelId)
        {
            List<UserHandler> connections = openConnections.GetOrAdd(channelId, (_) => []);

            foreach (UserHandler connection in connections)
            {
                await connection.SendMessage(message);
            }
        }

        public async void DeleteMessage(int messageId, int channelId)
        {
            List<UserHandler> connections = openConnections.GetOrAdd(channelId, (_) => []);

            foreach (UserHandler connection in connections)
            {
                await connection.DeleteMessage(messageId);
            }
        }

    }

    class UserHandler
    {
        private ConnectionManager manager;

        private readonly SemaphoreSlim _sendLock;
        private WebSocket websocket;
        private int channelId;

        private readonly TaskCompletionSource<object> _complete;
        public int userId { get; }

        public UserHandler(WebSocket ws, int cId, int uId, ConnectionManager manager, TaskCompletionSource<object> complete)
        {
            channelId = cId;
            websocket = ws;
            userId = uId;
            _sendLock = new SemaphoreSlim(1, 1); //Websockets can only have 1 Send at a time. Since the webserver is multithreaded, it's not
            //guaranteed that only one message will be sent a time. Therefore, a lock needs to be used to ensure that only one Send is called at a time.
            //SemphaoreSlim is a type of lock.

            _complete = complete;

            this.manager = manager;

            manager.RegisterSession(this, cId);
        }

        public async Task SendMessage(Message msg)
        {
            await _sendLock.WaitAsync();

            if (websocket.State != WebSocketState.Open && websocket.State != WebSocketState.Connecting)
            {
                this.Dispose();
                return;
            }

            try
            {
                await websocket.SendAsync(Encoding.UTF8.GetBytes(msg.ToJson()), WebSocketMessageType.Text, true, CancellationToken.None);
                _sendLock.Release();
            }
            catch (Exception)
            {
                //assume that the websocket has been non gracefully closed, just get rid of it.
                this.Dispose();
            }
        }

        public async Task DeleteMessage(int messageId)
        {
            await _sendLock.WaitAsync();

            if (websocket.State != WebSocketState.Open && websocket.State != WebSocketState.Connecting)
            {
                this.Dispose();
                return;
            }

            try
            {
                //delete message
                //this is a JSON string with one parameter. This is more performant than creating a class to hold just one data type.
                await websocket.SendAsync(Encoding.UTF8.GetBytes($"{{\"messageId\": {messageId}}}"), WebSocketMessageType.Text, true, CancellationToken.None);
                _sendLock.Release(); //this doesn't need to go into finally because any errors will result in the connection being disposed
            }
            catch (Exception)
            {
                //assume that the websocket has been non gracefully closed, just get rid of it.
                this.Dispose();
            }
        }

        /// <summary>
        /// Close and dispose of the web socket handler
        /// </summary>
        public void Dispose()
        {
            manager.RemoveSession(this, channelId);

            _complete.SetResult(true);
            websocket.Dispose();
            _sendLock.Dispose();
        }
    }

}

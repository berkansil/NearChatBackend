using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NearChatBackend
{
    internal class Websocket
    {
        private static Dictionary<int, List<Websocket>> OpenConnections = new Dictionary<int, List<Websocket>>();
        public class Message(int messageId, string content, DateTime sendDate, int authorId, int channelId, int? replyingMessageId)
        {
            public int MessageId = messageId;
            public string Content = content;
            public DateTime SendDate = sendDate;
            public int AuthorId = authorId;
            public int ChannelId = channelId;
            public int? ReplyingMessageId = replyingMessageId;
        }

        private void PropogateEvent(string serialisedEvent)
        {
            
        }


        public static void DeleteMessageInChannel(int channelId, int messageId)
        {

        }

        public static void SendMessageToChannel(int channelId, Message msg)
        {

        }

        public static void OpenConnection(IPAddress address, int channelId) { }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

using MySqlConnector;
using System.Buffers.Text;
using System.Data;
using System.Data.Common;
using System.Transactions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Authentication;
using System.Security;

namespace NearChatBackend
{
    public enum UserTableColumns
    {
        UserId,
        About,
        Username,
        LastActive,
        ProfilePicture,
        Status
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Status
    {
        Online = 1,
        Idle,
        DoNotDisturb,
        Offline
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChannelType
    {
        Neighbourhood,
        Local,
        City,
        Country,
        Direct
    }

    [JsonSerializable(typeof(User))]
    internal partial class UserSerialisation : JsonSerializerContext
    {

    }
    public class User
    {
        [JsonInclude] public int UserId;
        [JsonInclude] public string? About;
        [JsonInclude] public string Username;
        [JsonInclude] public DateTime? LastActive;
        [JsonInclude] public string? ProfilePicture;
        [JsonInclude] public Status? Status;

        public User(int userId, string? about, string username, DateTime? lastActive, string? profilePicture, Status? status)
        {
            UserId = userId;
            About = about;
            Username = username;
            LastActive = lastActive;
            ProfilePicture = profilePicture;
            Status = status;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, typeof(User), UserSerialisation.Default);
        }
    }

    [JsonSerializable(typeof(Message))]
    internal partial class MessageSerialisation : JsonSerializerContext
    {

    }
    public class Message
    {
        [JsonInclude] public int MessageId;
        [JsonInclude] public string Content;
        [JsonInclude] public DateTime SendDate;
        [JsonInclude] public int AuthorId;
        [JsonInclude] public int? ReplyingMessageId;
        [JsonInclude] public int ChannelId;

        public Message(int messageId, string content, DateTime sendDate, int authorId, int? replyingMessageId, int channelId)
        {
            MessageId = messageId;
            Content = content;
            SendDate = sendDate;
            AuthorId = authorId;
            ReplyingMessageId = replyingMessageId;
            ChannelId = channelId;
        }
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, typeof(Message), MessageSerialisation.Default);
        }
    }

    [JsonSerializable(typeof(Channel))]
    internal partial class ChannelSerialisation : JsonSerializerContext
    {
         
    }
    public class Channel
    {
        [JsonInclude] public int ChannelId;
        [JsonInclude] public string Name;
        [JsonInclude] public ChannelType ChannelType;

        public Channel(int channelId, string name, ChannelType channelType)
        {
            ChannelId = channelId;
            Name = name;
            ChannelType = channelType;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, typeof(Channel), ChannelSerialisation.Default);
        }
    }

    internal class Database
    {

        private string connectionString;
        public Database(string serverIp, string userId, string password)
        {
            connectionString = $"Server={serverIp};User ID={userId};Password={password};Database=nearchat";

            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            if (connection.State != System.Data.ConnectionState.Open || !connection.Ping())
            {
                throw new ArgumentException("Cannot connect to database");
            }
        }
        static private int GenerateUserId()
        {
            Random rnd = new Random();
            return rnd.Next(1000000, 1999999);
        }

        static private int GenerateMessageId()
        {
            Random rnd = new Random();
            return rnd.Next(3000000, 9999999);
        }

        static private int GenerateChannelId()
        {
            Random rnd = new Random();
            return rnd.Next(2000000, 2999999);
        }

        static private bool HasSpecialChars(string yourString)
        {
            return yourString.Any(ch => !char.IsLetterOrDigit(ch));
        }

        /// <summary>
        /// Generate a token and add it to db as a valid token for the passed user id
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="userId"></param>
        /// <param name="transaction"></param>
        /// <returns>The unhashed token generated, or null if there is an error</returns>
        static private string? GenerateAndAddToken(MySqlConnection connection, int userId, MySqlTransaction? transaction)
        {
            //Generate the token
            (string token, string tokenSha256) = GenerateSessionToken();

            //Add to database
            string tokenQuery = "INSERT INTO Tokens (UserId, Token) VALUES (@UserId, @Token)";
            using MySqlCommand tokenCommand = new MySqlCommand(tokenQuery, connection, transaction);
            tokenCommand.Parameters.AddWithValue("@UserId", userId);
            tokenCommand.Parameters.AddWithValue("@Token", tokenSha256);

            try
            {
                tokenCommand.ExecuteNonQuery();
                return token;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return null;
            }
        }
        static private string? GenerateAndAddToken(MySqlConnection connection, int userId)
        {
            return GenerateAndAddToken(connection, userId, null);
        }



        /// <summary>
        /// Generate a random session token
        /// </summary>
        /// <returns>A base64 string of the token, and a base64 string of a sha256 hash of the token</returns>
        static private (string, string) GenerateSessionToken()
        {
            byte[] token = new byte[32];

            RandomNumberGenerator.Fill(token);

            byte[] sha256Token = SHA256.HashData(token);

            return (Convert.ToBase64String(token), Convert.ToBase64String(sha256Token));
        }

        /// <summary>
        /// Hash a password using Pbkdf2, generating the salt
        /// </summary>
        /// <param name="password"></param>
        /// <returns>The password hash and the salt</returns>
        static private (byte[], byte[]) HashPassword(string password, byte[]? salt)
        {
            //Create a blank byte array to represent the salt
            if (salt == null)
            {
                salt = new byte[32];
                //Use a cryptographic RNG to populate the salt with random numbers
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(salt);
            }

            //Convert the password string into its UTF8 bytes
            byte[] passwordBytes = UTF8Encoding.UTF8.GetBytes(password);

            //Generate the hash, using a length of 60, 2 iterations and the SHA256 hashing algorithm
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, 2, HashAlgorithmName.SHA256, 60);

            return (hash, salt);
        }

        private object? GetUserDataFromToken(string tokenString, UserTableColumns column)
        {
            byte[] token = Convert.FromBase64String(tokenString);
            byte[] sha256Token = SHA256.HashData(token);

            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            string query = $"SELECT Users.{column.ToString()} FROM Tokens JOIN Users ON Users.UserId = Tokens.UserId WHERE Token = @Token";
            using MySqlCommand command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Token", Convert.ToBase64String(sha256Token));

            try
            {
                return command.ExecuteScalar();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return null;
            }
        }

        private object?[]? GetMultipleUserDataFromToken(string tokenString, UserTableColumns[] columns)
        {
            byte[] token = Convert.FromBase64String(tokenString);
            byte[] sha256Token = SHA256.HashData(token);

            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            string query = "SELECT Users." + columns[0].ToString();
            for (int i = 1; i < columns.Length; i++)
            {
                query += ", Users." + columns[i].ToString();
            }
            query += " FROM Tokens JOIN Users ON Users.UserId = Tokens.UserId WHERE Token = @Token";
            using MySqlCommand command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Token", Convert.ToBase64String(sha256Token));

            using MySqlDataReader reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            object?[] recordData = new object?[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                recordData[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            return recordData;
        }

        public int? GetUserIdFromToken(string tokenString)
        {
            return (int?)GetUserDataFromToken(tokenString, UserTableColumns.UserId);
        }

        public string? GetUserNameFromToken(string tokenString)
        {
            return (string?)GetUserDataFromToken(tokenString, UserTableColumns.Username);
        }

        private bool CanUserAccessChannel(MySqlConnection conection, int userId, int channelId)
        {
            try
            {
                string permissionQuery = "SELECT EXISTS (SELECT 1 FROM UserChannels WHERE UserId = @UserId AND ChannelId = @ChannelId)";
                using MySqlCommand permissionCheck = new MySqlCommand(permissionQuery, conection);
                permissionCheck.Parameters.AddWithValue("@UserId", userId);

                return Convert.ToBoolean(permissionCheck.ExecuteScalar());
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Get a user's details from their user id. If no user id is passed, the calling user is returned
        /// </summary>
        /// <param name="token"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public User GetUserDetails(string token, int? userId)
        {
            //Validate the token 
            int? tokenUserId = GetUserIdFromToken(token);
            if (!tokenUserId.HasValue)
            {
                throw new AuthenticationException("Invalid token");
                //return (DatabaseResponses.InvalidToken, null);
            }

            //Pick the right user id
            if (!userId.HasValue)
            {
                userId = tokenUserId;
            }

            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            string getUserQuery = "SELECT * FROM Users WHERE UserId = @UserId";
            using MySqlCommand command = new MySqlCommand(getUserQuery, connection);
            command.Parameters.AddWithValue("@UserId", userId);

            using MySqlDataReader reader = command.ExecuteReader();
                
            //Get the first (and only) record, return an error if no record is returned.
            if (!reader.Read())
            {
                throw new ArgumentException("User not found");
            }

            int targetUserId = reader.GetInt32(0);
            string? about = reader.IsDBNull(1) ? null : reader.GetString(1);
            string username = reader.GetString(2);
            DateTime? lastActive = reader.IsDBNull(3) ? null : reader.GetDateTime(3);
            string? profilePictureUrl = reader.IsDBNull(4) ? null : reader.GetString(4);
            string? status = reader.IsDBNull(5) ? null : reader.GetString(5);

            Status enumStatus;
            if(!Enum.TryParse<Status>(status, out enumStatus))
            {
                enumStatus = Status.Offline;
            }

            User requestedUser = new(targetUserId, about, username, lastActive, profilePictureUrl, enumStatus);
            return requestedUser;
            
        }

        public bool UpdateStatus(string token, Status newStatus)
        {
            int? userId = GetUserIdFromToken(token);
            if (!userId.HasValue)
            {
                return false;
            }

            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            string updateQuery = "UPDATE Users SET Status = @Status, LastActive = @LastActive WHERE UserId = @UserId";
            using MySqlCommand cmd = new MySqlCommand(updateQuery, connection);
            cmd.Parameters.AddWithValue("@Status", newStatus);
            cmd.Parameters.AddWithValue("@LastActive", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@UserId", userId.Value);

            try
            {
                if (cmd.ExecuteNonQuery() == 1)
                {
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool UpdateProfilePicture(string token, string url)
        {
            if (url.Length > 255)
            {
                return false;
            }
            int? userId = GetUserIdFromToken(token);
            if (!userId.HasValue)
            {
                return false;
            }

            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            string updateQuery = "UPDATE Users SET ProfilePicture = @ProfilePicture WHERE UserId = @UserId";
            using MySqlCommand cmd = new MySqlCommand(updateQuery, connection);
            cmd.Parameters.AddWithValue("@ProfilePicture", url);
            cmd.Parameters.AddWithValue("@UserId", userId.Value);

            try
            {
                if (cmd.ExecuteNonQuery() == 1)
                {
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool UpdateAbout(string token, string about)
        {
            if (about.Length > 255)
            {
                return false;
            }

            int? userId = GetUserIdFromToken(token);
            if (!userId.HasValue)
            {
                return false;
            }

            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            string updateQuery = "UPDATE Users SET About = @About WHERE UserId = @UserId";
            using MySqlCommand cmd = new MySqlCommand(updateQuery, connection);
            cmd.Parameters.AddWithValue("@About", about);
            cmd.Parameters.AddWithValue("@UserId", userId.Value);

            try
            {
                if (cmd.ExecuteNonQuery() == 1)
                {
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool LeaveChannel(string token, int channelId)
        {
            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            int? userId = GetUserIdFromToken(token);
            if (!userId.HasValue)
            {
                return false; //invalid token
            }

            //Remove record from UserChannels
            string removeQuery = "DELETE FROM UserChannels WHERE UserId = @UserId AND ChannelId = @ChannelId";
            using MySqlCommand command = new MySqlCommand(removeQuery, connection);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@ChannelId", channelId);

            try
            {
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool JoinChannel(string token, int channelId)
        {
            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            int? userId = GetUserIdFromToken(token);
            if (!userId.HasValue)
            {
                return false; //invalid token
            }

            //Add the record to UserChannels
            string addQuery = "INSERT INTO UserChannels (UserId, ChannelId) VALUES (@UserId, @ChannelId)";
            using MySqlCommand command = new MySqlCommand(addQuery, connection);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@ChannelId", channelId);

            try
            {
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }

        public int? CreateChannel(string channelName, ChannelType type)
        {
            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            string channelQuery = "INSERT INTO Channels (ChannelId, Name, ChannelType) VALUES (@ChannelId, @Name, @ChannelType)";
            while (true)
            {
                int channelIdCandidate = GenerateChannelId();
                using MySqlCommand command = new MySqlCommand(channelQuery, connection);
                command.Parameters.AddWithValue("@ChannelId", channelIdCandidate);
                command.Parameters.AddWithValue("@Name", channelName);
                command.Parameters.AddWithValue("@ChannelType", type);

                try
                {
                    command.ExecuteNonQuery();
                    return channelIdCandidate;
                } catch(MySqlException ex)
                {
                    if (ex.Number == 1062) // Duplicate entry error
                    {
                        string errorMessage = ex.Message;
                        if (errorMessage.Contains("for key 'messages.PRIMARY'"))
                        {
                            //Generate a new user id for the next iteration
                            channelIdCandidate = GenerateChannelId();
                            continue;
                        }
                    }
                    return null;
                }
            }
        }

        public string AuthenticateUser(string username, string password)
        {
            if (username.Length < 3 || username == null)
            {
                throw new ArgumentException("Username cannot be less than 4 characters");
            }

            if (username.Length > 32)
            {
                throw new ArgumentException("Username cannot be longer than 32 characters");
            }

            if (HasSpecialChars(username))
            {
                throw new ArgumentException("Username cannot contain special characters");
            }

            if (password == null || password.Length < 5)
            {
                throw new ArgumentException("Password must be at least 6 characters");
            }

            if (password.Length > 128)
            {
                throw new ArgumentException("Password cannot be longer than 128 characters");
            }

            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            string authQuery = "SELECT Password, Salt, Users.UserId FROM Users JOIN Credentials ON Users.UserId = Credentials.UserId WHERE Username = @Username";
            using MySqlCommand command = new MySqlCommand(authQuery, connection);
            command.Parameters.AddWithValue("@Username", username);

            using MySqlDataReader reader = command.ExecuteReader();
            if (reader == null || !reader.Read())
            {
                throw new ArgumentException("Invalid credentials");
            }

            byte[] passwordHash = new byte[60];
            reader.GetBytes(0, 0, passwordHash, 0, 60);
            byte[] salt = new byte[32];
            reader.GetBytes(1, 0, salt, 0, 32);

            int userId = reader.GetInt32(2);

            (byte[] enteredHash, _) = HashPassword(password, salt);
            if (!passwordHash.SequenceEqual(enteredHash))
            {
                throw new ArgumentException("Invalid credentials");
            }

            //Generate and add the token
            string? token = GenerateAndAddToken(connection, userId);
            if (token == null)
            {
                throw new Exception();
            }
            return token;

        }

        public bool Logout(string token)
        {
            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            string deleteQuery = "DELETE FROM Tokens WHERE Token = @Token";
            using MySqlCommand command = new MySqlCommand(deleteQuery, connection);
            command.Parameters.AddWithValue("@Username", token);

            if (command.ExecuteNonQuery() >= 1)
            {
                return true;
            }
            return false;
        }

        public List<Message> GetMessages(string token, int channelId, int? lastMessageId)
        {
            //Validate token
            int? requsterId = GetUserIdFromToken(token);
            if (!requsterId.HasValue)
            {
                throw new AuthenticationException("Invalid token");
            }

            using MySqlConnection connection = new(connectionString);
            connection.Open();

            //Validate that the user can access the channel
            if (!CanUserAccessChannel(connection, requsterId.Value, channelId))
            {
                throw new SecurityException("No permission");
            }


            string getMessagesQuery;
            DateTime? timestamp = null;

            if (!lastMessageId.HasValue)
            {
                getMessagesQuery = "SELECT MessageId, Content, SendDate, AuthorId, ReplyingMessageId FROM Messages WHERE ChannelId = @ChannelId ORDER BY SendDate DESC LIMIT 20";
            }
            else
            {
                getMessagesQuery = "SELECT MessageId, Content, SendDate, AuthorId, ReplyingMessageId FROM Messages WHERE ChannelId = @ChannelId AND (SendDate < @Timestamp OR (SendDate = @Timestamp AND MessageId < @MessageId)) ORDER BY SendDate DESC, MessageId DESC LIMIT 20";

                //Get the message timestamp (and validate it's in the right channel)
                string getMessageInfo = "SELECT ChannelId, SendDate FROM Messages WHERE MessageId = @MessageId";
                using MySqlCommand msgDetailsCmd = new(getMessageInfo, connection);
                msgDetailsCmd.Parameters.AddWithValue("@MessageId", lastMessageId.Value);

                using MySqlDataReader reader = msgDetailsCmd.ExecuteReader();
                if (!reader.Read())
                {
                    throw new ArgumentException("Invalid last message");
                }

                int cid = reader.GetInt32(0);
                if (cid != channelId)
                {
                    throw new ArgumentException("Invalid last message");
                }
                timestamp = reader.GetDateTime(1);
            }

            using MySqlCommand command = new(getMessagesQuery, connection);
            command.Parameters.AddWithValue("@ChannelId", channelId);
            if (timestamp != null)
            {
                command.Parameters.AddWithValue("@Timestamp", timestamp);
            }
            if (lastMessageId.HasValue)
            {
                command.Parameters.AddWithValue("@MessageId", lastMessageId.Value);
            }

            using MySqlDataReader rdr = command.ExecuteReader();

            List<Message> messages = new List<Message>();
            while (rdr.Read())
            {
                int msgId = rdr.GetInt32(0);
                string content = rdr.GetString(1);
                DateTime msgTimestamp = rdr.GetDateTime(2);
                int authorId = rdr.GetInt32(3);
                int? replyingMessageId = rdr.GetInt32(4);

                messages.Add(new Message(msgId, content, msgTimestamp, authorId, replyingMessageId, channelId));
            }

            return messages;

        }

        public bool DeleteMessage(string token, int messageId)
        {
            using MySqlConnection connection = new(connectionString);
            connection.Open();

            //Get the matching user id from the token
            int? requesterId = GetUserIdFromToken(token);

            if (!requesterId.HasValue)
            {
                return false;
            }

            //Get the AuthorId and ChannelId matching the message from the database
            string getChannelQuery = "SELECT AuthorId, ChannelId FROM Messages WHERE MessageId = @MessageId";
            using MySqlCommand getChannel = new MySqlCommand(getChannelQuery, connection);
            getChannel.Parameters.AddWithValue("@MessageId", messageId);

            using MySqlDataReader rdr = getChannel.ExecuteReader();
            if (!rdr.Read()) //If there is no matching message
            {
                return false;
            }

            int authorId = rdr.GetInt32(0);
            int channelId = rdr.GetInt32(1);

            //Check that the authorId matches the requesterId
            if (authorId != requesterId)
            {
                return false;
            }

            //Delete the message
            string deleteQuery = "DELETE FROM Messages WHERE MessageId = @MessageId";
            using MySqlCommand command = new MySqlCommand(deleteQuery, connection);
            command.Parameters.AddWithValue("@MessageId", messageId);

            if (command.ExecuteNonQuery() == 0)
            {
                return false;
            }
            Websocket.DeleteMessageInChannel(channelId, messageId);
            return true;
        }

        public bool SendMessage(string token, string message, int channelId, int? parentId)
        {
            if (message == null || message.Length == 0 || message.Length > 4096)
            {
                return false;
            }

            int? userId = GetUserIdFromToken(token);
            if (userId == null)
            {
                return false;
            }

            using MySqlConnection connection = new(connectionString);
            connection.Open();

            if (!CanUserAccessChannel(connection, userId.Value, channelId))
            {
                return false;
            }

            int messageIdCandidate = GenerateMessageId();

            string insertQuery = "INSERT INTO Messages (MessageId, Content, SendDate, AuthorId, ReplyingMessageId, ChannelId) VALUES (@MessageId, @Content, @SendDate, @AuthorId, @ReplyingMessageId, @ChannelId)";
            DateTime time = DateTime.UtcNow;

            while (true)
            {
                try
                {
                    using MySqlCommand command = new MySqlCommand(insertQuery, connection);
                    command.Parameters.AddWithValue("@MessageId", messageIdCandidate);
                    command.Parameters.AddWithValue("@Content", message);
                    command.Parameters.AddWithValue("@SendDate", time);
                    command.Parameters.AddWithValue("@AuthorId", userId);
                    command.Parameters.AddWithValue("@ReplyingMessageId", parentId);
                    command.Parameters.AddWithValue("@ChannelId", channelId);

                    command.ExecuteNonQuery();
                    Websocket.SendMessageToChannel(channelId, new Websocket.Message(messageIdCandidate, message, time, (int)userId, channelId, parentId));
                    return true;
                }
                catch (MySqlException ex)
                {
                    if (ex.Number == 1062) // Duplicate entry error
                    {
                        string errorMessage = ex.Message;
                        if (errorMessage.Contains("for key 'messages.PRIMARY'"))
                        {
                            //Generate a new user id for the next iteration
                            messageIdCandidate = GenerateMessageId();
                            continue;
                        }
                    }
                    return false;
                }
            }
        }

        public List<Channel> GetChannels(string token)
        {
            int? userId = GetUserIdFromToken(token);
            if (!userId.HasValue)
            {
                throw new AuthenticationException("Invalid token");
            }

            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();

            string getChannelsQuery = "SELECT Channels.* FROM UserChannels INNER JOIN Channels ON UserChannels.ChannelId = Channels.ChannelId WHERE UserChannels.UserId = @UserId";
            using MySqlCommand command = new MySqlCommand(getChannelsQuery, connection);
            command.Parameters.AddWithValue("@UserId", userId.Value);

            List<Channel> channels = new List<Channel>();
            MySqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int channelId = reader.GetInt32(0);
                string channelName = reader.GetString(1);
                string channelTypeString = reader.GetString(2);

                ChannelType type = Enum.Parse<ChannelType>(channelTypeString);

                channels.Add(new Channel(channelId, channelName, type));
            }
            return channels;
        }

        /// <summary>
        /// Register a new user
        /// </summary>
        /// <returns>A valid authentication token if registered successfully</returns>
        public string RegisterUser(string username, string password)
        {
            if (username.Length < 3 || username == null)
            {
                throw new ArgumentException("Username cannot be less than 4 characters");
            }

            if (username.Length > 32)
            {
                throw new ArgumentException("Username cannot be longer than 32 characters");
            }

            if (HasSpecialChars(username))
            {
                throw new ArgumentException("Username cannot contain special characters");
            }

            if (password == null || password.Length < 5)
            {
                throw new ArgumentException("Password must be at least 6 characters");
            }

            if (password.Length > 128)
            {
                throw new ArgumentException("Password cannot be longer than 128 characters");
            }

            using MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();
            using MySqlTransaction transaction = connection.BeginTransaction();

            string insertQuery = "INSERT INTO Users (UserId, Username) VALUES (@UserId, @Username)";

            int userIdCandidate = GenerateUserId();

            while (true)
            {
                try
                {
                    //Create the command using the most up to date variables
                    using MySqlCommand command = new MySqlCommand(insertQuery, connection, transaction);
                    command.Parameters.AddWithValue("@UserId", userIdCandidate);
                    command.Parameters.AddWithValue("@Username", username);

                    command.ExecuteNonQuery();
                    //Execution will only reach this point if the command executed successfully, so break out of the loop and 
                    break;
                }
                catch (MySqlException ex) //the only exception that can be encountered from the code above is a MySqlException, so no need to handle other types
                {
                    if (ex.Number == 1062) // Duplicate entry error
                    {
                        string errorMessage = ex.Message;
                        if (errorMessage.Contains("for key 'users.PRIMARY'"))
                        {
                            //Generate a new user id for the next iteration
                            userIdCandidate = GenerateUserId();
                        }
                        else
                        {
                            transaction.Rollback();
                            throw new ArgumentException("User already exists");
                        }
                    }
                    else
                    {
                        //If there was a database error which cannot be handled
                        Console.WriteLine($"MySQL Error {ex.Number}: {ex.Message}");
                        transaction.Rollback();
                        throw new Exception();
                    }
                }
            }

            //By this point, the User entry in Users was created successfully. Now I only need to create the crdentials entry and the tokens entry.
            try
            {
                (byte[] hash, byte[] salt) = HashPassword(password, null);

                //Insert into the database
                string credentialsQuery = "INSERT INTO Credentials (UserId, Password, Salt) VALUES (@UserId, @Password, @Salt)";
                using var command = new MySqlCommand(credentialsQuery, connection, transaction);
                command.Parameters.AddWithValue("@UserId", userIdCandidate);
                command.Parameters.AddWithValue("@Password", hash);
                command.Parameters.AddWithValue("@Salt", salt);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                //Rollback all changes (including creating the user)
                transaction.Rollback();
                //Log the error
                Console.WriteLine(ex.ToString());
                throw new Exception();
            }

            string? token = GenerateAndAddToken(connection, userIdCandidate, transaction);
            if (token == null)
            {
                transaction.Rollback();
                Console.WriteLine("failed to generate token");
                throw new Exception();
            }

            transaction.Commit();
            return token;
        }

        
    }
}
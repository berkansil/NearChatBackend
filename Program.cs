using NearChatBackend;
using System.Text.Json.Serialization;

namespace NearChat
{
    [JsonSourceGenerationOptions(
       WriteIndented = true,
       PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
       GenerationMode = JsonSourceGenerationMode.Serialization)]
    [JsonSerializable(typeof(User))]
    internal partial class SerializationModeOptionsContext : JsonSerializerContext
    {
    }

    internal class Program
    {
        static void Main(string[] args)
        {

            Database db = new Database("127.0.0.1", "root", "1234");
            ConnectionManager websocketManager = new(db);
            db.AddWebsocketManager(websocketManager);
            RestAPI main_api = new RestAPI("https://127.0.0.1:443/", db, websocketManager);
            main_api.Start();

            /*
            (Database.DatabaseResponses response, string? token) =  db.RegisterUser("TestUser2", "testPassword");

            if (response == Database.DatabaseResponses.Success)
            {
                Console.WriteLine(token);
            }
            else
            {
                Console.WriteLine(response);
            }
            */

            //
            //db.AuthenticateUser("TestUser2", "testPassword");
            //Console.ReadLine();
            string token = "LtfNrOwqqVxlZNqZSBMvtU/Og0aprfpzhLO5ai5LJxA=";
            Console.WriteLine(db.UpdateStatus(token, Status.Online));

            db.UpdateAbout(token, "Test about message");
            db.UpdateProfilePicture(token, "https://example.com");
            //string invalidToken = "LtfNrOwqqVxlZNqZSBMvtU/Og0aprfpzhLO5ai5LJvB="

            User usr = db.GetUserDetails(token, null);
            if (usr != null)
            {
                Console.WriteLine(usr.ToJson());
            }
        }
    }
}
